namespace Player.App;

/// <summary>Turns wheel deltas into whole steps. A mouse notch is 1.0, but precision touchpads and free-spinning
/// wheels send many small deltas; counting each one as a step made the volume jump by 30 % per swipe.</summary>
public sealed class WheelSteps
{
    double _rest;

    public int Take(double delta)
    {
        if (Math.Sign(delta) != Math.Sign(_rest)) _rest = 0; // changing direction starts over
        _rest += delta;
        var steps = (int)Math.Truncate(_rest);
        _rest -= steps;
        return steps;
    }
}
