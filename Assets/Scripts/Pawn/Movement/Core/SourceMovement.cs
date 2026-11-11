using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The maths of Half-Life 2's player movement, ported from Source SDK 2013 gamemovement.cpp. Pure functions with no
/// physics queries, so they can be tested outside Unity. They work in any consistent units: pass speeds and
/// distances in the units the velocity is in.
/// </summary>
public static class SourceMovement
{
    /// <summary>Friction(): above <paramref name="stopSpeed"/> speed decays in proportion to itself; below it, at a
    /// fixed rate, so the pawn comes to a stop instead of creeping.</summary>
    public static Vector3 ApplyFriction(Vector3 velocity, float friction, float stopSpeed, float minSpeed, float dt)
    {
        float speed = velocity.magnitude;
        if (speed < minSpeed)
            return velocity;

        float control = speed < stopSpeed ? stopSpeed : speed;
        float drop = control * friction * dt;
        float newSpeed = Mathf.Max(speed - drop, 0f);
        return velocity * (newSpeed / speed);
    }

    /// <summary>Accelerate(): adds speed along <paramref name="wishDir"/> until the speed along it reaches
    /// <paramref name="wishSpeed"/>, at most accel * wishSpeed * dt per tick. Speed in other directions is untouched.</summary>
    public static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float dt)
    {
        float currentSpeed = Vector3.Dot(velocity, wishDir);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0f)
            return velocity;

        float accelSpeed = Mathf.Min(accel * dt * wishSpeed, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    /// <summary>AirAccelerate(): as <see cref="Accelerate"/>, but speed along the wish direction only goes up to
    /// <paramref name="airSpeedCap"/>, while the step size still uses the full wish speed. Turning the wish direction
    /// away from the velocity keeps that cap out of reach, which is how air-strafing gains speed.</summary>
    public static Vector3 AirAccelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed, float accel, float airSpeedCap, float dt)
    {
        float cappedWishSpeed = Mathf.Min(wishSpeed, airSpeedCap);
        float currentSpeed = Vector3.Dot(velocity, wishDir);
        float addSpeed = cappedWishSpeed - currentSpeed;
        if (addSpeed <= 0f)
            return velocity;

        float accelSpeed = Mathf.Min(accel * wishSpeed * dt, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    /// <summary>The forward and side move the input asks for (cl_forwardspeed and cl_sidespeed times the stick), scaled
    /// down together when longer than <paramref name="maxSpeed"/> (CGameMovement::CheckParameters).</summary>
    public static Vector2 InputMove(Vector2 stick, float inputSpeed, float maxSpeed)
    {
        Vector2 move = new(Mathf.Clamp(stick.x, -1f, 1f) * inputSpeed, Mathf.Clamp(stick.y, -1f, 1f) * inputSpeed);
        float speed = Mathf.Sqrt(move.x * move.x + move.y * move.y);
        if (speed > maxSpeed && speed > 0f)
            move *= maxSpeed / speed;
        return move;
    }

    /// <summary>The flat wish velocity for a move (x: side, y: forward) facing <paramref name="yaw"/>.</summary>
    public static Vector3 WishVelocity(Quaternion yaw, Vector2 move)
    {
        Vector3 forward = Flat(yaw * Vector3.forward);
        Vector3 right = Flat(yaw * Vector3.right);
        return forward * move.y + right * move.x;
    }

    /// <summary>Splits a wish velocity into direction and speed, with the speed capped at <paramref name="maxSpeed"/>.</summary>
    public static void SplitWish(Vector3 wishVelocity, float maxSpeed, out Vector3 wishDir, out float wishSpeed)
    {
        wishSpeed = wishVelocity.magnitude;
        wishDir = wishSpeed > 0f ? wishVelocity / wishSpeed : Vector3.zero;
        if (wishSpeed > maxSpeed)
            wishSpeed = maxSpeed;
    }

    /// <summary>HL2 single player's jump bonus (CheckJumpButton, HL2_DLL): a share of the forward move, added along the
    /// view's forward, capped so it doesn't push flat speed past maxSpeed * (1 + share). The cap can make the bonus
    /// negative, and backwards moves flip its sign: together, that is accelerated back-hopping.</summary>
    public static Vector3 JumpForwardBoost(Vector3 velocity, Quaternion yaw, float forwardMove, float maxSpeed, bool sprintingOrDucked)
    {
        Vector3 forward = Flat(yaw * Vector3.forward);
        float share = sprintingOrDucked ? 0.1f : 0.5f;
        float addition = Mathf.Abs(forwardMove * share);
        float maxBoostedSpeed = maxSpeed + maxSpeed * share;
        float newSpeed = addition + new Vector2(velocity.x, velocity.z).magnitude;

        if (newSpeed > maxBoostedSpeed)
            addition -= newSpeed - maxBoostedSpeed;

        if (forwardMove < 0f)
            addition = -addition;

        return velocity + forward * addition;
    }

    /// <summary>ClipVelocity(): removes the part of the velocity going into a surface. An overbounce of 1 slides along it.</summary>
    public static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal, float overbounce = 1f)
    {
        float backoff = Vector3.Dot(velocity, normal) * overbounce;
        Vector3 result = velocity - normal * backoff;

        // Iterate once to make sure we aren't still moving through the plane
        float adjust = Vector3.Dot(result, normal);
        if (adjust < 0f)
            result -= normal * adjust;

        return result;
    }

    /// <summary>
    /// TryPlayerMove()'s velocity handling for the surfaces a move ran into: slide along the first plane the result
    /// doesn't push into any other; failing that, along the crease of exactly two planes; failing that, stop.
    /// Velocity that ends up against the original direction stops too, so the pawn never bounces back.
    /// </summary>
    public static Vector3 ClipToPlanes(Vector3 velocity, IReadOnlyList<Vector3> planes)
    {
        if (planes.Count == 0)
            return velocity;

        Vector3 primal = velocity;
        Vector3 result = Vector3.zero;
        bool found = false;

        for (int i = 0; i < planes.Count && !found; i++)
        {
            Vector3 clipped = ClipVelocity(velocity, planes[i]);
            bool clearOfOthers = true;
            for (int j = 0; j < planes.Count; j++)
            {
                if (j != i && Vector3.Dot(clipped, planes[j]) < 0f)
                {
                    clearOfOthers = false;
                    break;
                }
            }

            if (clearOfOthers)
            {
                result = clipped;
                found = true;
            }
        }

        if (!found)
        {
            if (planes.Count != 2)
                return Vector3.zero;

            Vector3 crease = Vector3.Cross(planes[0], planes[1]).normalized;
            result = crease * Vector3.Dot(crease, velocity);
        }

        return Vector3.Dot(result, primal) <= 0f ? Vector3.zero : result;
    }

    /// <summary>Clamps each component to ±<paramref name="maxVelocity"/> (CheckVelocity()).</summary>
    public static Vector3 ClampVelocity(Vector3 velocity, float maxVelocity) => new(
        Mathf.Clamp(velocity.x, -maxVelocity, maxVelocity),
        Mathf.Clamp(velocity.y, -maxVelocity, maxVelocity),
        Mathf.Clamp(velocity.z, -maxVelocity, maxVelocity));

    /// <summary>Source's SimpleSpline: eases the crouch view in and out.</summary>
    public static float SimpleSpline(float x)
    {
        x = Mathf.Clamp(x, 0f, 1f);
        return 3f * x * x - 2f * x * x * x;
    }

    private static Vector3 Flat(Vector3 v)
    {
        v.y = 0f;
        return v.normalized;
    }
}
