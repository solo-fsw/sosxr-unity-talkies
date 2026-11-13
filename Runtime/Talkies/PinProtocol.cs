using System.Globalization;


namespace SOSXR.Talkies
{
    /// <summary>The kind of a response line received from the pin device.</summary>
    public enum PinResponseKind
    {
        /// <summary>Confirmation of a SET command: <c>OK,SET,&lt;pin&gt;,&lt;0|1&gt;</c>.</summary>
        PinSet,

        /// <summary>Answer to a GET command: <c>OK,GET,&lt;pin&gt;,&lt;0|1&gt;</c>.</summary>
        PinGet,

        /// <summary>The device reported an error: <c>ERR,...</c>.</summary>
        Error,

        /// <summary>A SET or GET line whose pin or value is not a valid integer.</summary>
        Malformed,

        /// <summary>Anything else, such as the PONG answer to a PING.</summary>
        Other
    }


    /// <summary>A parsed response line from the pin device.</summary>
    public readonly struct PinResponse
    {
        /// <summary>Creates a parsed response.</summary>
        public PinResponse(PinResponseKind kind, int pin, bool value, string raw)
        {
            Kind = kind;
            Pin = pin;
            Value = value;
            Raw = raw;
        }


        /// <summary>What kind of response this is.</summary>
        public PinResponseKind Kind { get; }

        /// <summary>The pin number. Only valid for <see cref="PinResponseKind.PinSet" /> and <see cref="PinResponseKind.PinGet" />.</summary>
        public int Pin { get; }

        /// <summary>The pin value (<c>true</c> = HIGH). Only valid for <see cref="PinResponseKind.PinSet" /> and <see cref="PinResponseKind.PinGet" />.</summary>
        public bool Value { get; }

        /// <summary>The original line, trimmed.</summary>
        public string Raw { get; }
    }


    /// <summary>
    ///     Formats commands for, and parses responses from, the simple CSV pin protocol used by <see cref="PinController" />.
    ///     Commands: <c>SET,&lt;pin&gt;,&lt;0|1&gt;</c>, <c>GET,&lt;pin&gt;</c> and <c>PING</c>.
    ///     Responses: <c>&lt;status&gt;,&lt;command&gt;[,&lt;pin&gt;,&lt;value&gt;]</c>, where status is <c>OK</c> or <c>ERR</c>.
    ///     Pure C#, no Unity dependencies, so it can be unit tested.
    /// </summary>
    public static class PinProtocol
    {
        /// <summary>Builds a SET command (without trailing newline).</summary>
        public static string FormatSet(int pin, bool value)
        {
            return "SET," + pin.ToString(CultureInfo.InvariantCulture) + "," + (value ? "1" : "0");
        }


        /// <summary>Builds a GET command (without trailing newline).</summary>
        public static string FormatGet(int pin)
        {
            return "GET," + pin.ToString(CultureInfo.InvariantCulture);
        }


        /// <summary>
        ///     Parses one response line. Never throws: lines that cannot be parsed come back as
        ///     <see cref="PinResponseKind.Malformed" /> or <see cref="PinResponseKind.Other" />.
        /// </summary>
        public static PinResponse Parse(string line)
        {
            var raw = line?.Trim() ?? string.Empty;
            var parts = raw.Split(',');

            if (parts.Length < 2)
            {
                return new PinResponse(PinResponseKind.Other, -1, false, raw);
            }

            var status = parts[0].Trim();
            var command = parts[1].Trim();

            if (status == "ERR")
            {
                return new PinResponse(PinResponseKind.Error, -1, false, raw);
            }

            if ((command == "SET" || command == "GET") && parts.Length >= 4)
            {
                if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pin) ||
                    !int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                {
                    return new PinResponse(PinResponseKind.Malformed, -1, false, raw);
                }

                var kind = command == "SET" ? PinResponseKind.PinSet : PinResponseKind.PinGet;

                return new PinResponse(kind, pin, value == 1, raw);
            }

            return new PinResponse(PinResponseKind.Other, -1, false, raw);
        }


        /// <summary>Renames <c>true</c>/<c>false</c> to <c>HIGH</c>/<c>LOW</c>, which is more in line with GPIO lingo.</summary>
        public static string HighLow(bool value)
        {
            return value ? "HIGH" : "LOW";
        }
    }
}
