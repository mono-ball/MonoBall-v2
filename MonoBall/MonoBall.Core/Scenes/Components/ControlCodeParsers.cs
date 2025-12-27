using System;
using Microsoft.Xna.Framework;

namespace MonoBall.Core.Scenes.Components
{
    /// <summary>
    /// Factory and registry for control code parsers.
    /// Provides default parsers and allows registration of custom parsers.
    /// </summary>
    public static class ControlCodeParsers
    {
        private static readonly System.Collections.Generic.Dictionary<
            string,
            IControlCodeParser
        > _parsers = new System.Collections.Generic.Dictionary<string, IControlCodeParser>(
            StringComparer.OrdinalIgnoreCase
        );

        /// <summary>
        /// Initializes default control code parsers.
        /// </summary>
        static ControlCodeParsers()
        {
            RegisterDefaultParsers();
        }

        /// <summary>
        /// Registers the default control code parsers.
        /// </summary>
        private static void RegisterDefaultParsers()
        {
            Register(new PauseUntilPressParser());
            Register(new ResetParser());
            Register(new ClearParser());
            Register(new PauseParser());
            Register(new ColorParser());
            Register(new ShadowParser());
            Register(new SpeedParser());
        }

        /// <summary>
        /// Registers a control code parser.
        /// </summary>
        /// <param name="parser">The parser to register.</param>
        /// <exception cref="ArgumentNullException">Thrown if parser is null.</exception>
        public static void Register(IControlCodeParser parser)
        {
            if (parser == null)
            {
                throw new ArgumentNullException(nameof(parser));
            }

            _parsers[parser.ControlCodeName] = parser;
        }

        /// <summary>
        /// Unregisters a control code parser.
        /// </summary>
        /// <param name="controlCodeName">The control code name to unregister.</param>
        public static void Unregister(string controlCodeName)
        {
            if (string.IsNullOrEmpty(controlCodeName))
            {
                return;
            }

            _parsers.Remove(controlCodeName);
        }

        /// <summary>
        /// Tries to parse a control code using registered parsers.
        /// </summary>
        /// <param name="controlCode">The control code string (without braces).</param>
        /// <param name="originalPosition">The original position in the text string.</param>
        /// <param name="token">The parsed token if successful.</param>
        /// <returns>True if a parser was found and parsing succeeded, false otherwise.</returns>
        public static bool TryParse(string controlCode, int originalPosition, out TextToken token)
        {
            token = default;

            if (string.IsNullOrEmpty(controlCode))
            {
                return false;
            }

            // Try exact match first (for non-parameterized codes)
            if (
                _parsers.TryGetValue(controlCode, out var exactParser)
                && !exactParser.IsParameterized
            )
            {
                try
                {
                    token = exactParser.Parse(controlCode, originalPosition);
                    return true;
                }
                catch (FormatException)
                {
                    return false;
                }
            }

            // Try parameterized parsers (check if control code starts with parser name)
            foreach (var kvp in _parsers)
            {
                var parser = kvp.Value;
                if (parser.IsParameterized)
                {
                    string prefix = parser.ControlCodeName + ":";
                    if (
                        controlCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || controlCode.Equals(
                            parser.ControlCodeName,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        try
                        {
                            token = parser.Parse(controlCode, originalPosition);
                            return true;
                        }
                        catch (FormatException)
                        {
                            // Continue to next parser
                            continue;
                        }
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Parser for PAUSE_UNTIL_PRESS control code.
    /// </summary>
    internal class PauseUntilPressParser : IControlCodeParser
    {
        public string ControlCodeName => "PAUSE_UNTIL_PRESS";
        public bool IsParameterized => false;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.Equals("PAUSE_UNTIL_PRESS", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'PAUSE_UNTIL_PRESS', got '{controlCode}'");
            }

            return new TextToken
            {
                TokenType = TextTokenType.PauseUntilPress,
                Value = null,
                OriginalPosition = originalPosition,
            };
        }
    }

    /// <summary>
    /// Parser for RESET control code.
    /// </summary>
    internal class ResetParser : IControlCodeParser
    {
        public string ControlCodeName => "RESET";
        public bool IsParameterized => false;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.Equals("RESET", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'RESET', got '{controlCode}'");
            }

            return new TextToken
            {
                TokenType = TextTokenType.Reset,
                Value = null,
                OriginalPosition = originalPosition,
            };
        }
    }

    /// <summary>
    /// Parser for CLEAR control code.
    /// </summary>
    internal class ClearParser : IControlCodeParser
    {
        public string ControlCodeName => "CLEAR";
        public bool IsParameterized => false;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.Equals("CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'CLEAR', got '{controlCode}'");
            }

            return new TextToken
            {
                TokenType = TextTokenType.Clear,
                Value = null,
                OriginalPosition = originalPosition,
            };
        }
    }

    /// <summary>
    /// Parser for PAUSE:seconds control code.
    /// </summary>
    internal class PauseParser : IControlCodeParser
    {
        public string ControlCodeName => "PAUSE";
        public bool IsParameterized => true;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.StartsWith("PAUSE:", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'PAUSE:seconds', got '{controlCode}'");
            }

            string secondsStr = controlCode.Substring(6); // Skip "PAUSE:"
            if (
                !float.TryParse(
                    secondsStr,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float seconds
                )
                || seconds < 0
            )
            {
                throw new FormatException(
                    $"Invalid pause duration: '{controlCode}'. Expected non-negative number (seconds)."
                );
            }

            return new TextToken
            {
                TokenType = TextTokenType.Pause,
                Value = seconds,
                OriginalPosition = originalPosition,
            };
        }
    }

    /// <summary>
    /// Parser for COLOR:r,g,b control code.
    /// </summary>
    internal class ColorParser : IControlCodeParser
    {
        public string ControlCodeName => "COLOR";
        public bool IsParameterized => true;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.StartsWith("COLOR:", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'COLOR:r,g,b', got '{controlCode}'");
            }

            string colorStr = controlCode.Substring(6); // Skip "COLOR:"
            string[] parts = colorStr.Split(',');
            if (
                parts.Length != 3
                || !byte.TryParse(parts[0].Trim(), out byte r)
                || !byte.TryParse(parts[1].Trim(), out byte g)
                || !byte.TryParse(parts[2].Trim(), out byte b)
            )
            {
                throw new FormatException(
                    $"Invalid color format: '{controlCode}'. Expected COLOR:r,g,b where r,g,b are 0-255."
                );
            }

            Color colorValue = new Color(r, g, b, (byte)255);
            return new TextToken
            {
                TokenType = TextTokenType.Color,
                Value = colorValue,
                OriginalPosition = originalPosition,
            };
        }
    }

    /// <summary>
    /// Parser for SHADOW:r,g,b control code.
    /// </summary>
    internal class ShadowParser : IControlCodeParser
    {
        public string ControlCodeName => "SHADOW";
        public bool IsParameterized => true;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.StartsWith("SHADOW:", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'SHADOW:r,g,b', got '{controlCode}'");
            }

            string shadowStr = controlCode.Substring(7); // Skip "SHADOW:"
            string[] parts = shadowStr.Split(',');
            if (
                parts.Length != 3
                || !byte.TryParse(parts[0].Trim(), out byte r)
                || !byte.TryParse(parts[1].Trim(), out byte g)
                || !byte.TryParse(parts[2].Trim(), out byte b)
            )
            {
                throw new FormatException(
                    $"Invalid shadow color format: '{controlCode}'. Expected SHADOW:r,g,b where r,g,b are 0-255."
                );
            }

            Color shadowValue = new Color(r, g, b, (byte)255);
            return new TextToken
            {
                TokenType = TextTokenType.Shadow,
                Value = shadowValue,
                OriginalPosition = originalPosition,
            };
        }
    }

    /// <summary>
    /// Parser for SPEED:frames control code.
    /// </summary>
    internal class SpeedParser : IControlCodeParser
    {
        public string ControlCodeName => "SPEED";
        public bool IsParameterized => true;

        public TextToken Parse(string controlCode, int originalPosition)
        {
            if (!controlCode.StartsWith("SPEED:", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException($"Expected 'SPEED:frames', got '{controlCode}'");
            }

            string speedStr = controlCode.Substring(6); // Skip "SPEED:"
            if (!int.TryParse(speedStr, out int speed) || speed < 0)
            {
                throw new FormatException(
                    $"Invalid speed value: '{controlCode}'. Expected non-negative integer."
                );
            }

            return new TextToken
            {
                TokenType = TextTokenType.Speed,
                Value = speed,
                OriginalPosition = originalPosition,
            };
        }
    }
}
