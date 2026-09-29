namespace Gravitone.Core;

internal enum BounceKind { None, Launch, Attention }

/// <summary>
/// The Dock's icon bounce. Launch: steady bounces until the app shows a window.
/// Attention: bursts of three higher bounces with a pause, until the app is brought forward.
/// A stop request always lets the current bounce land first.
/// </summary>
internal sealed class Bouncer
{
    const double LaunchPeriod = 0.62, LaunchHeight = 0.5, LaunchTimeout = 12;
    const double AttentionPeriod = 0.5, AttentionHeight = 0.65, AttentionPause = 1.1;
    const int AttentionBounces = 3;

    double _start;
    bool _stopping;
    long _stopAfterBounce;

    public BounceKind Kind { get; private set; }
    public bool IsActive => Kind != BounceKind.None;

    public void Start(BounceKind kind, double now)
    {
        if (kind == BounceKind.None) return;
        if (Kind == kind)
        {
            _stopping = false;
            return;
        }
        Kind = kind;
        _start = now;
        _stopping = false;
    }

    public void Stop(double now)
    {
        if (!IsActive || _stopping) return;
        _stopping = true;
        _stopAfterBounce = BounceIndex(now - _start);
    }

    /// <summary>Current lift, as a fraction of the icon size.</summary>
    public double Offset(double now)
    {
        if (!IsActive) return 0;
        double t = now - _start;
        if (Kind == BounceKind.Launch && t > LaunchTimeout && !_stopping) Stop(now);

        bool resting = Kind == BounceKind.Attention && t % Cycle >= AttentionBounces * AttentionPeriod;
        if (_stopping && (resting || BounceIndex(t) > _stopAfterBounce))
        {
            Kind = BounceKind.None;
            return 0;
        }
        if (resting) return 0;

        double period = Kind == BounceKind.Launch ? LaunchPeriod : AttentionPeriod;
        double height = Kind == BounceKind.Launch ? LaunchHeight : AttentionHeight;
        double phase = (Kind == BounceKind.Launch ? t : t % Cycle) % period / period;
        return height * 4 * phase * (1 - phase); // a parabola: a real jump
    }

    static double Cycle => AttentionBounces * AttentionPeriod + AttentionPause;

    long BounceIndex(double t) => Kind == BounceKind.Launch
        ? (long)(t / LaunchPeriod)
        : (long)(t / Cycle) * AttentionBounces + Math.Min(AttentionBounces - 1, (long)(t % Cycle / AttentionPeriod));
}
