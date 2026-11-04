using System;
using System.Collections.Generic;
using System.Globalization;

namespace Amethyst.Look
{
    /// <summary>An sRGB colour as drawn on screen, 8 bits a channel.</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public readonly byte R,
            G,
            B;

        public Rgb(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        /// <summary>From "#rrggbb".</summary>
        public static Rgb Hex(string hex)
        {
            var s = hex.TrimStart('#');
            if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, null, out var v))
                throw new FormatException($"Rgb: '{hex}' is not #rrggbb");
            return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        public bool Equals(Rgb o) => R == o.R && G == o.G && B == o.B;

        public override bool Equals(object obj) => obj is Rgb o && Equals(o);

        public override int GetHashCode() => R << 16 | G << 8 | B;

        public override string ToString() => $"#{R:x2}{G:x2}{B:x2}";
    }

    /// <summary>A floor zone's grid colours: its resting lines and their pulse.</summary>
    public readonly struct Zone
    {
        public readonly string Name;
        public readonly Rgb Line,
            Pulse;

        public Zone(string name, Rgb line, Rgb pulse)
        {
            Name = name;
            Line = line;
            Pulse = pulse;
        }
    }

    /// <summary>
    /// The colour roles (docs/game-design.md, Look &gt; Color roles), from Playlist Survivors'
    /// rhythm_champions/Content/palette.json: one object every drawer reads. White is the
    /// hero's and everything the hero makes; enemies come in three families of two hues; grey
    /// is a wall's. The value ladder (no monster outshines the hero) is the shaders' job.
    /// </summary>
    public sealed class Palette
    {
        /// <summary>The void: the floor and the monsters' glass.</summary>
        public Rgb Void = Rgb.Hex("#030408"),
            Glass = Rgb.Hex("#08090d");

        /// <summary>The hero, its slashes, suites and statuses.</summary>
        public Rgb Hero = Rgb.Hex("#f4f8ff");

        /// <summary>Fodder: the key family.</summary>
        public Rgb Cyan = Rgb.Hex("#05c9ff"),
            Blue = Rgb.Hex("#457dff");

        /// <summary>Special types: the accent family.</summary>
        public Rgb Magenta = Rgb.Hex("#ff38b6"),
            Violet = Rgb.Hex("#d52eff");

        /// <summary>Heavies and the Warden: the counter family.</summary>
        public Rgb Orange = Rgb.Hex("#ff6524"),
            Amber = Rgb.Hex("#ffa629");

        /// <summary>The Heartwyrm.</summary>
        public Rgb Boss = Rgb.Hex("#ff2e17");

        /// <summary>Hazards and enemy attacks: filled red and orange shapes.</summary>
        public Rgb Hazard = Rgb.Hex("#ff3218"),
            EnemyAttack = Rgb.Hex("#ff5018");

        public Rgb Wall = Rgb.Hex("#8c8c8c");

        /// <summary>The floor grid's zone keys.</summary>
        public Zone Cobalt = new Zone("cobalt", Rgb.Hex("#001f67"), Rgb.Hex("#1443b3")),
            Teal = new Zone("teal", Rgb.Hex("#00323c"), Rgb.Hex("#005769")),
            Ember = new Zone("ember", Rgb.Hex("#421f00"), Rgb.Hex("#7d3a00"));

        public static readonly Palette Default = new Palette();

        /// <summary>A crowd type's hue by its roster id (the knight is the hero's white); an
        /// unknown type is fodder cyan.</summary>
        public Rgb ForType(string id) =>
            id switch
            {
                "knight" => Hero,
                "skeleton" or "slime" => Cyan,
                "demon" or "bat" => Blue,
                "goblin" => Magenta,
                "mushroom" => Violet,
                "bigOrk" or "fatOgre" => Orange,
                "troll" => Amber,
                "dragon" => Boss,
                _ => Cyan,
            };

        public IEnumerable<Zone> Zones()
        {
            yield return Cobalt;
            yield return Teal;
            yield return Ember;
        }
    }
}
