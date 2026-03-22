using System.Numerics;
using BmSDK;

namespace Online.Core;

public class InterpolationBuffer
{
    private const int Capacity = 10;
    private const float InterpolationDelay = 0.1f; // 100ms delay for smooth interpolation

    private readonly TransformSnapshot[] _buffer = new TransformSnapshot[Capacity];
    private int _writeIndex = 0;
    private int _count = 0;

    public void AddSnapshot(Vector3 position, Rotator rotation, double timestamp)
    {
        _buffer[_writeIndex] = new TransformSnapshot(position, rotation, timestamp);
        _writeIndex = (_writeIndex + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public (Vector3 Position, Rotator Rotation) Interpolate(double currentTime)
    {
        if (_count == 0)
        {
            return (Vector3.Zero, default);
        }

        // Target time is slightly in the past to allow interpolation
        double targetTime = currentTime - InterpolationDelay;

        // Find the two snapshots to interpolate between
        TransformSnapshot? before = null;
        TransformSnapshot? after = null;

        for (int i = 0; i < _count; i++)
        {
            int idx = (_writeIndex - 1 - i + Capacity) % Capacity;
            var snapshot = _buffer[idx];

            if (snapshot.Timestamp <= targetTime)
            {
                before = snapshot;
                // Get the next one as 'after' if it exists
                if (i > 0)
                {
                    int afterIdx = (_writeIndex - i + Capacity) % Capacity;
                    after = _buffer[afterIdx];
                }
                break;
            }
            after = snapshot;
        }

        // If we only have future snapshots, use the oldest one
        if (before == null && after != null)
        {
            return (after.Value.Position, after.Value.Rotation);
        }

        // If we only have past snapshots, extrapolate from the most recent
        if (before != null && after == null)
        {
            return (before.Value.Position, before.Value.Rotation);
        }

        // If we have both, interpolate
        if (before != null && after != null)
        {
            double duration = after.Value.Timestamp - before.Value.Timestamp;
            if (duration <= 0) return (after.Value.Position, after.Value.Rotation);

            float t = (float)((targetTime - before.Value.Timestamp) / duration);
            t = Math.Clamp(t, 0f, 1f);

            return (
                Vector3.Lerp(before.Value.Position, after.Value.Position, t),
                LerpRotator(before.Value.Rotation, after.Value.Rotation, t)
            );
        }

        // Fallback - shouldn't happen
        return (Vector3.Zero, default);
    }

    private static Rotator LerpRotator(Rotator a, Rotator b, float t)
    {
        // Simple lerp - for large rotations would need proper angle interpolation
        return new Rotator
        {
            Pitch = (int)(a.Pitch + (b.Pitch - a.Pitch) * t),
            Yaw = (int)(a.Yaw + (b.Yaw - a.Yaw) * t),
            Roll = (int)(a.Roll + (b.Roll - a.Roll) * t)
        };
    }

    private readonly record struct TransformSnapshot(
        Vector3 Position,
        Rotator Rotation,
        double Timestamp
    );
}
