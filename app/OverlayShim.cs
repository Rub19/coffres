namespace ChestCounter;

public static class OverlayAppearance
{
    public static readonly int[] Sizes = { 70, 80, 90, 100, 115, 130, 150, 175 };
    public static readonly int[] Opacities = { 100, 90, 80, 70, 60, 50, 40, 30 };
    public static int ClampSize(int percent) => Math.Clamp(percent, Sizes[0], Sizes[^1]);
    public static int ClampOpacity(int percent) => Math.Clamp(percent, Opacities[^1], 100);
}

public static class OverlayModel
{
    public static string LastMatchCaption => L.T("dernière partie", "last match", "última partida");
    public static string SquadLabel => L.T("Équipe", "Squad", "Equipo");
}
