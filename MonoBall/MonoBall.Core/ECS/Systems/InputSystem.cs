using System;
using Arch.Core;
using Arch.System;
using MonoBall.Core;
using MonoBall.Core.ECS.Components;
using MonoBall.Core.ECS.Input;
using MonoBall.Core.ECS.Services;
using Serilog;

namespace MonoBall.Core.ECS.Systems
{
    /// <summary>
    /// System that processes keyboard and gamepad input and converts it to movement requests.
    /// Implements Pokemon-style grid-locked input with queue-based buffering for responsive controls.
    /// </summary>
    /// <remarks>
    /// Movement validation and collision checking happens in MovementSystem.
    /// Input blocking is handled via IInputBlocker (e.g., when console has ExclusiveInput=true).
    /// </remarks>
    public class InputSystem : BaseSystem<World, float>
    {
        private readonly IInputBlocker _inputBlocker;
        private readonly InputBuffer _inputBuffer;
        private readonly IInputBindingService _inputBindingService;
        private readonly ILogger _logger;
        private readonly QueryDescription _playerQuery;

        // Cache to prevent duplicate buffering
        private Direction _lastBufferedDirection = Direction.None;
        private float _lastBufferTime = -1f;
        private float _totalTime;

        /// <summary>
        /// Initializes a new instance of the InputSystem.
        /// </summary>
        /// <param name="world">The ECS world.</param>
        /// <param name="inputBlocker">The input blocker service (can be NullInputBlocker).</param>
        /// <param name="inputBuffer">The input buffer service.</param>
        /// <param name="inputBindingService">The input binding service for named input actions.</param>
        /// <param name="logger">The logger for logging operations.</param>
        public InputSystem(
            World world,
            IInputBlocker inputBlocker,
            InputBuffer inputBuffer,
            IInputBindingService inputBindingService,
            ILogger logger
        )
            : base(world)
        {
            _inputBlocker = inputBlocker ?? throw new ArgumentNullException(nameof(inputBlocker));
            _inputBuffer = inputBuffer ?? throw new ArgumentNullException(nameof(inputBuffer));
            _inputBindingService =
                inputBindingService ?? throw new ArgumentNullException(nameof(inputBindingService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _playerQuery = new QueryDescription().WithAll<
                PlayerComponent,
                PositionComponent,
                GridMovement,
                InputState,
                DirectionComponent
            >();
        }

        /// <summary>
        /// Updates the input system, processing input and creating movement requests.
        /// </summary>
        /// <param name="deltaTime">The elapsed time since last update in seconds.</param>
        public override void Update(in float deltaTime)
        {
            _totalTime += deltaTime;

            // Update input binding service state
            _inputBindingService.Update();

            // Check if input is blocked by a higher-priority scene (e.g., console with ExclusiveInput=true)
            if (_inputBlocker.IsInputBlocked)
            {
                return;
            }

            // Process input for all players
            World.Query(
                in _playerQuery,
                (
                    Entity entity,
                    ref PlayerComponent player,
                    ref PositionComponent position,
                    ref GridMovement movement,
                    ref InputState inputState,
                    ref DirectionComponent directionComponent
                ) =>
                {
                    if (!inputState.InputEnabled)
                    {
                        return;
                    }

                    // Get current input direction from named input actions
                    Direction currentDirection = _inputBindingService.GetMovementDirection();

                    // Update InputState with named actions
                    UpdateInputStateActions(ref inputState);

                    // Pokemon Emerald-style running state logic
                    if (currentDirection == Direction.None)
                    {
                        // No input - set to not moving (only if not mid-movement and not turning in place)
                        // Don't cancel turn-in-place when key is released - let the animation complete
                        if (
                            !movement.IsMoving
                            && movement.RunningState != RunningState.TurnDirection
                        )
                        {
                            movement.RunningState = RunningState.NotMoving;
                        }

                        inputState.PressedDirection = Direction.None;
                        directionComponent.Value = Direction.None;
                    }
                    else
                    {
                        inputState.PressedDirection = currentDirection;
                        directionComponent.Value = currentDirection;

                        // Check if we need to turn in place first (Pokemon Emerald behavior)
                        // CRITICAL: Compare against BOTH MovementDirection AND FacingDirection
                        // This matches pokeemerald/src/field_player_avatar.c:588:
                        //   direction != GetPlayerMovementDirection() && runningState != MOVING
                        // Turn triggers only when input differs from BOTH:
                        // - MovementDirection: last actual movement (prevents turn when returning to last move dir)
                        // - FacingDirection: current visual facing (prevents restart when holding same direction)
                        if (
                            currentDirection != movement.MovementDirection
                            && currentDirection != movement.FacingDirection
                            && movement.RunningState != RunningState.Moving
                            && !movement.IsMoving
                        )
                        {
                            // Turn in place - start turn animation
                            // DON'T buffer input here - only move if key is still held when turn completes
                            movement.StartTurnInPlace(currentDirection);
                            _logger.Debug(
                                "Turning in place to {To} (from MovementDir: {MovementDir}, FacingDir: {Facing})",
                                currentDirection,
                                movement.MovementDirection,
                                movement.FacingDirection
                            );
                        }
                        else if (movement.RunningState != RunningState.TurnDirection)
                        {
                            // Either already facing correct direction or already moving - allow movement
                            // BUT only if not currently in turn-in-place state (wait for turn to complete)
                            movement.RunningState = RunningState.Moving;

                            // Buffer input if:
                            // 1. Not currently moving (allows holding keys for continuous movement), OR
                            // 2. Direction changed (allows queuing direction changes during movement)
                            // But only if we haven't buffered this exact direction very recently (prevents duplicates)
                            bool shouldBuffer =
                                !movement.IsMoving || currentDirection != _lastBufferedDirection;

                            // Also prevent buffering the same direction multiple times per frame
                            bool isDifferentTiming =
                                _totalTime != _lastBufferTime
                                || currentDirection != _lastBufferedDirection;

                            if (shouldBuffer && isDifferentTiming)
                            {
                                if (_inputBuffer.AddInput(currentDirection, _totalTime))
                                {
                                    _lastBufferedDirection = currentDirection;
                                    _lastBufferTime = _totalTime;
                                    _logger.Debug(
                                        "Buffered input direction: {Direction}",
                                        currentDirection
                                    );
                                }
                            }
                        }
                        // else: RunningState == TurnDirection - wait for turn animation to complete
                        // Don't buffer input during turn (allows tap-to-turn), but still process action button and consume buffered input
                    }

                    // Check for action button
                    inputState.ActionPressed = _inputBindingService.IsActionPressed(
                        InputAction.Interact
                    );

                    // Try to consume buffered input and create MovementRequest
                    // CRITICAL: Only consume when not moving (matches oldmonoball line 187-190)
                    // This works correctly because:
                    // 1. RunningState now stays as Moving after movement completes (not reset by MovementSystem)
                    // 2. When movement completes (IsMoving=false), we consume buffer immediately
                    // 3. MovementSystem processes MovementRequest in same or next frame
                    if (
                        !movement.IsMoving
                        && _inputBuffer.TryConsumeInput(_totalTime, out Direction bufferedDirection)
                    )
                    {
                        // Use component pooling: reuse existing component or add new one
                        if (World.Has<MovementRequest>(entity))
                        {
                            ref MovementRequest request = ref World.Get<MovementRequest>(entity);
                            if (!request.Active)
                            {
                                request.Direction = bufferedDirection;
                                request.Active = true;
                                _logger.Debug(
                                    "Consumed buffered input: {Direction}",
                                    bufferedDirection
                                );
                                _lastBufferedDirection = Direction.None;
                            }
                        }
                        else
                        {
                            World.Add(entity, new MovementRequest(bufferedDirection));
                            _logger.Debug(
                                "Consumed buffered input: {Direction}",
                                bufferedDirection
                            );
                            _lastBufferedDirection = Direction.None;
                        }
                    }
                }
            );
        }

        /// <summary>
        /// Updates InputState with named input actions (architecture improvement).
        /// </summary>
        private void UpdateInputStateActions(ref InputState inputState)
        {
            // Clear previous frame's just-pressed/just-released
            inputState.JustPressedActions.Clear();
            inputState.JustReleasedActions.Clear();

            // Update pressed actions
            inputState.PressedActions.Clear();
            foreach (InputAction action in Enum.GetValues<InputAction>())
            {
                if (_inputBindingService.IsActionPressed(action))
                {
                    inputState.PressedActions.Add(action);
                }

                if (_inputBindingService.IsActionJustPressed(action))
                {
                    inputState.JustPressedActions.Add(action);
                }

                if (_inputBindingService.IsActionJustReleased(action))
                {
                    inputState.JustReleasedActions.Add(action);
                }
            }
        }
    }
}
