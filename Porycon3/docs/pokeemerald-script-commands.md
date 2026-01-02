# pokeemerald Script Commands → CSX Mapping

Analysis of scripts from: LittlerootTown*, Route101, OldaleTown

## Command Categories

### ✅ Supported (Have CSX Equivalent)

| pokeemerald Command | CSX Equivalent | Notes |
|---------------------|----------------|-------|
| `msgbox TEXT, MSGBOX_DEFAULT` | `Context.Apis.MessageBox.ShowMessage(text)` | Basic dialogue |
| `msgbox TEXT, MSGBOX_NPC` | `Context.Apis.MessageBox.ShowMessage(text)` | Same, auto face+lock |
| `msgbox TEXT, MSGBOX_SIGN` | `Context.Apis.MessageBox.ShowMessage(text)` | Sign-style box |
| `msgbox TEXT, MSGBOX_YESNO` | **MISSING** - Need `IMessageBoxApi.ShowYesNo()` | Returns YES/NO result |
| `message TEXT` | `Context.Apis.MessageBox.ShowMessage(text)` | Raw message display |
| `waitmessage` | Handled internally | Wait for message close |
| `closemessage` | Handled internally | Close message box |
| `faceplayer` | `FacePlayer(evt.PlayerEntity)` | NPC faces player |
| `lock` | `Context.Apis.Movement.LockMovement(entity)` | Lock NPC movement |
| `lockall` | **PARTIAL** - Need to lock all entities | Lock all movement |
| `release` | `Context.Apis.Movement.UnlockMovement(entity)` | Unlock NPC |
| `releaseall` | **PARTIAL** - Need to unlock all | Unlock all |
| `end` | `return;` / method end | Script termination |
| `setflag FLAG` | `Context.Apis.Flags.SetVariable(flag, true)` | Set game flag |
| `clearflag FLAG` | `Context.Apis.Flags.SetVariable(flag, false)` | Clear game flag |
| `checkflag FLAG` | `Context.Apis.Flags.GetVariable<bool>(flag)` | Check flag status |
| `setvar VAR, VALUE` | `Context.Apis.Flags.SetVariable(var, value)` | Set variable |
| `compare VAR, VALUE` | `Context.Apis.Flags.GetVariable<int>(var) == value` | Compare variable |
| `goto_if_eq LABEL` | `if (var == value) { ... }` | Conditional branch |
| `goto_if_ne LABEL` | `if (var != value) { ... }` | Conditional branch |
| `goto_if_set FLAG` | `if (flag) { ... }` | Flag conditional |
| `goto_if_unset FLAG` | `if (!flag) { ... }` | Flag conditional |
| `goto LABEL` | Method call or control flow | Jump to label |
| `call LABEL` | Method call | Call subroutine |
| `return` | `return;` | Return from call |
| `delay N` | **MISSING** - Need `await Task.Delay(ms)` or timer | Delay in frames |
| `checkplayergender` | **MISSING** - Need `IPlayerApi.GetGender()` | Check male/female |

### 🟡 Movement Commands (Partially Supported)

| pokeemerald Command | CSX Equivalent | Status |
|---------------------|----------------|--------|
| `applymovement OBJ, MOVEMENT` | **MISSING** - Need `IMovementApi.ApplyMovementSequence()` | Complex |
| `waitmovement 0` | **MISSING** - Need movement completion callback | Wait for moves |
| `walk_down`, `walk_up`, etc. | Need movement queue system | Individual steps |
| `walk_fast_*` | Need running state | Fast walking |
| `walk_in_place_faster_*` | `Context.Apis.Npc.FaceDirection()` | Turn in place |
| `face_*` | `Context.Apis.Npc.FaceDirection()` | Face direction |
| `step_end` | Movement terminator | End of sequence |
| `delay_8`, `delay_16` | Timer-based delays | Frame delays |
| `jump_*` | **MISSING** - Need jump animation | Jump movements |
| `set_invisible` | **MISSING** - Need visibility control | Hide entity |
| `lock_facing_direction` | **MISSING** - Need facing lock | Lock facing |
| `unlock_facing_direction` | **MISSING** | Unlock facing |
| `disable_jump_landing_ground_effect` | **MISSING** | VFX control |

### 🔴 Object/Entity Commands (Need New APIs)

| pokeemerald Command | CSX Equivalent | Priority |
|---------------------|----------------|----------|
| `addobject LOCALID` | **MISSING** - Need `IEntityApi.SpawnEntity()` | HIGH |
| `removeobject LOCALID` | **MISSING** - Need `IEntityApi.DestroyEntity()` | HIGH |
| `hideobjectat LOCALID, MAP` | **MISSING** - Need visibility control | MEDIUM |
| `showobjectat LOCALID, MAP` | **MISSING** | MEDIUM |
| `setobjectxy LOCALID, X, Y` | **MISSING** - Need `INpcApi.SetPosition()` | HIGH |
| `setobjectxyperm LOCALID, X, Y` | **MISSING** - Persistent position | HIGH |
| `setobjectmovementtype LOCALID, TYPE` | **MISSING** - Need behavior control | MEDIUM |
| `turnobject LOCALID, DIR` | `Context.Apis.Npc.FaceDirection()` | Supported |

### 🔴 Map/Warp Commands (Need New APIs)

| pokeemerald Command | CSX Equivalent | Priority |
|---------------------|----------------|----------|
| `warp MAP, X, Y` | `Context.Apis.Map.LoadMap()` + position | HIGH |
| `warpsilent MAP, X, Y` | **MISSING** - No fade transition | MEDIUM |
| `waitstate` | **MISSING** - Wait for warp/screen | MEDIUM |
| `fadescreen FADE_TO_BLACK` | **MISSING** - Need `IScreenApi` | MEDIUM |
| `setmaplayoutindex LAYOUT` | **MISSING** - Layout switching | LOW |
| `setmetatile X, Y, TILE, COLLISION` | **MISSING** - Tile modification | LOW |
| `hideplayer` | **MISSING** - Need player visibility | MEDIUM |

### 🔴 Audio Commands (Need New APIs)

| pokeemerald Command | CSX Equivalent | Priority |
|---------------------|----------------|----------|
| `playse SE_*` | **MISSING** - Need `IAudioApi.PlaySFX()` | HIGH |
| `waitse` | **MISSING** - Wait for SFX | MEDIUM |
| `playbgm MUS_*, LOOP` | **MISSING** - Need `IAudioApi.PlayBGM()` | HIGH |
| `fadedefaultbgm` | **MISSING** - BGM fade | MEDIUM |
| `savebgm MUS_*` | **MISSING** - Save current BGM | LOW |
| `playfanfare MUS_*` | **MISSING** - Need fanfare system | MEDIUM |
| `waitfanfare` | **MISSING** - Wait for fanfare | MEDIUM |

### 🔴 Door/Animation Commands (Need New APIs)

| pokeemerald Command | CSX Equivalent | Priority |
|---------------------|----------------|----------|
| `opendoor X, Y` | **MISSING** - Need `IDoorApi.Open()` | MEDIUM |
| `closedoor X, Y` | **MISSING** - Need `IDoorApi.Close()` | MEDIUM |
| `waitdooranim` | **MISSING** - Door animation wait | MEDIUM |

### 🔴 Item/Pokemon Commands (Need New APIs)

| pokeemerald Command | CSX Equivalent | Priority |
|---------------------|----------------|----------|
| `giveitem ITEM, COUNT` | **MISSING** - Need `IInventoryApi.GiveItem()` | HIGH |
| `givemon SPECIES, LEVEL` | **MISSING** - Need `IPartyApi.GivePokemon()` | HIGH |
| `special ChooseStarter` | **MISSING** - Need starter selection UI | MEDIUM |
| `special HealPlayerParty` | **MISSING** - Need `IPartyApi.HealAll()` | MEDIUM |
| `bufferspeciesname STR, SPECIES` | **MISSING** - String buffers | MEDIUM |
| `bufferleadmonspeciesname STR` | **MISSING** | MEDIUM |
| `showmonpic SPECIES, X, Y` | **MISSING** - Pokemon picture display | LOW |
| `hidemonpic` | **MISSING** | LOW |

### 🔴 Special/System Commands

| pokeemerald Command | CSX Equivalent | Priority |
|---------------------|----------------|----------|
| `special FUNC` | **MISSING** - Need special function registry | VARIES |
| `specialvar VAR, FUNC` | **MISSING** | VARIES |
| `switch VAR` | `switch (var)` in C# | Supported |
| `case VALUE, LABEL` | `case VALUE:` | Supported |
| `pokenavcall TEXT` | **MISSING** - PokeNav UI | LOW |

### 🔴 Map Script Triggers

| pokeemerald Command | CSX Equivalent | Notes |
|---------------------|----------------|-------|
| `map_script MAP_SCRIPT_ON_TRANSITION` | **MISSING** - Need `OnMapTransition` event | Map enter |
| `map_script MAP_SCRIPT_ON_FRAME_TABLE` | **MISSING** - Need frame-based triggers | Per-frame check |
| `map_script MAP_SCRIPT_ON_WARP_INTO_MAP_TABLE` | **MISSING** | Warp trigger |
| `map_script MAP_SCRIPT_ON_LOAD` | **MISSING** - Need `OnMapLoad` event | Map load |
| `map_script_2 VAR, VALUE, SCRIPT` | **MISSING** - Conditional triggers | State-based |

## Summary Statistics

| Category | Total | Supported | Partial | Missing |
|----------|-------|-----------|---------|---------|
| Dialogue | 6 | 4 | 1 | 1 |
| Control Flow | 12 | 12 | 0 | 0 |
| Flags/Variables | 6 | 6 | 0 | 0 |
| Movement | 15 | 2 | 3 | 10 |
| Entity/Object | 8 | 1 | 0 | 7 |
| Map/Warp | 7 | 1 | 0 | 6 |
| Audio | 7 | 0 | 0 | 7 |
| Items/Pokemon | 8 | 0 | 0 | 8 |
| Special | 6 | 2 | 0 | 4 |
| **TOTAL** | **75** | **28** | **4** | **43** |

## API Gaps to Fill

### High Priority (Needed for basic NPC interactions)
1. `IMessageBoxApi.ShowYesNo()` - Yes/No dialogue
2. `IAudioApi` - Sound effects and music
3. `IEntityApi` - Entity spawn/destroy/visibility
4. `INpcApi.SetPosition()` - Set entity position
5. `IInventoryApi` - Item give/take
6. `IPartyApi` - Pokemon give/heal

### Medium Priority (Needed for cutscenes)
1. `IMovementApi.ApplyMovementSequence()` - Scripted movement paths
2. `IScreenApi` - Fades, transitions
3. `IDoorApi` - Door animations
4. Movement completion events/callbacks
5. `IPlayerApi.GetGender()` - Player gender check

### Low Priority (Advanced features)
1. PokeNav integration
2. Map layout switching
3. Metatile modification
4. Pokemon picture display

## Example Translation

### pokeemerald (Simple NPC)
```asm
LittlerootTown_EventScript_Boy::
    msgbox LittlerootTown_Text_BirchSpendsDaysInLab, MSGBOX_NPC
    end
```

### CSX Equivalent
```csharp
public class LittlerootTownEventScriptBoy : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        OnInteraction(evt => {
            FacePlayer(evt.PlayerEntity);
            Context.Apis.MessageBox.ShowMessage(
                "PROF. BIRCH spends days in his LAB\n" +
                "studying, then he'll suddenly go out in\n" +
                "the wild to do more research…\n\n" +
                "When does PROF. BIRCH spend time\n" +
                "at home?"
            );
        });
    }
}
```

### pokeemerald (With Flag Check)
```asm
LittlerootTown_EventScript_Twin::
    lock
    faceplayer
    goto_if_set FLAG_ADVENTURE_STARTED, LittlerootTown_EventScript_GoodLuck
    goto_if_set FLAG_RESCUED_BIRCH, LittlerootTown_EventScript_YouSavedBirch
    goto_if_ne VAR_LITTLEROOT_TOWN_STATE, 0, LittlerootTown_EventScript_GoSaveBirch
    msgbox LittlerootTown_Text_IfYouGoInGrassPokemonWillJumpOut, MSGBOX_DEFAULT
    release
    end
```

### CSX Equivalent
```csharp
public class LittlerootTownEventScriptTwin : ScriptBase
{
    public override void RegisterEventHandlers(ScriptContext context)
    {
        OnInteraction(evt => {
            FacePlayer(evt.PlayerEntity);

            if (Context.Apis.Flags.GetVariable<bool>("FLAG_ADVENTURE_STARTED"))
            {
                Context.Apis.MessageBox.ShowMessage("Are you going to catch POKéMON?\nGood luck!");
                return;
            }

            if (Context.Apis.Flags.GetVariable<bool>("FLAG_RESCUED_BIRCH"))
            {
                Context.Apis.MessageBox.ShowMessage("You saved PROF. BIRCH!\nI'm so glad!");
                return;
            }

            var townState = Context.Apis.Flags.GetVariable<int>("VAR_LITTLEROOT_TOWN_STATE");
            if (townState != 0)
            {
                // GoSaveBirch dialogue
                Context.Apis.MessageBox.ShowMessage(
                    "Um, hi!\n\n" +
                    "There are scary POKéMON outside!\n" +
                    "I can hear their cries!\n\n" +
                    "Can you go see what's happening\nfor me?"
                );
                Context.Apis.Flags.SetVariable("VAR_LITTLEROOT_TOWN_STATE", 2);
                return;
            }

            Context.Apis.MessageBox.ShowMessage(
                "Um, um, um!\n\n" +
                "If you go outside and go in the grass,\n" +
                "wild POKéMON will jump out!"
            );
        });
    }
}
```

## Recommendations

1. **Phase 1**: Implement basic translator for simple `MSGBOX_NPC` and `MSGBOX_SIGN` scripts (covers ~40% of NPCs)
2. **Phase 2**: Add flag/variable support for branching dialogue
3. **Phase 3**: Add audio API for sound effects
4. **Phase 4**: Add movement sequence system for cutscenes
5. **Phase 5**: Add entity/item/pokemon APIs for complex events
