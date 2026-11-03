from amethyst_tools import flags

SOURCE = """
public enum SwordArc { Wide, ThrustLine = 3, [Obsolete] Spin2 }

public static class SwordFlags
{
    public static readonly Flag<SwordArc> Arc = Flag.Enum(
        "sword.arc",
        SwordArc.ThrustLine,
        "Swing shape, \\"wide\\" or not"
    );

    public static readonly Flag<float> Speed = Flag.Float("sword.speed", 1.5f, "Speed");
}
"""


def test_declarations_parse_as_csharpier_lays_them_out() -> None:
    found = [m.groupdict() for m in flags.DECL.finditer(SOURCE)]
    assert [(d["name"], d["kind"], d["default"]) for d in found] == [
        ("sword.arc", "Enum", "SwordArc.ThrustLine"),
        ("sword.speed", "Float", "1.5f"),
    ]
    assert len(flags.CALL.findall(SOURCE)) == 2
    assert flags.enums({"a.cs": SOURCE})["SwordArc"] == ("wide", "thrust-line", "spin2")
