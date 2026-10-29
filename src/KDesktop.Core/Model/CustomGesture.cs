namespace KDesktop.Core.Model;

/// <summary>
/// A mouse gesture drawn by the user (E4): its name (the hotkey is <c>Gesture</c> + name) and the strokes drawn to teach it,
/// already normalised by <see cref="Input.MouseGestures.Normalize"/> (x, y, x, y…).
/// </summary>
public sealed class CustomGesture
{
    public string Name { get; set; } = string.Empty;

    public List<double[]> Samples { get; set; } = [];
}
