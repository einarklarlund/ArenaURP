using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// <see cref="IMovementBody"/> on a CharacterController. The CharacterController does the collide-and-slide and
/// climbs steps; its owner forwards <c>OnControllerColliderHit</c> to <see cref="OnHit"/> so moves can report what
/// they ran into. Assumes the controller's center sits at half its height above the feet.
/// </summary>
public sealed class CharacterControllerBody : IMovementBody
{
    // Shrinks the probes so they start clear of the ground they rest on; a cast ignores what it starts inside.
    private const float ProbeShrink = 0.05f;
    // Ground contacts flatter than this only come from the rounded bottom touching an edge.
    private const float MinContactNormalY = 0.2f;

    private readonly CharacterController _controller;
    private readonly LayerMask _groundMask;
    private List<Vector3> _hitNormals;

    public CharacterControllerBody(CharacterController controller, LayerMask groundMask)
    {
        _controller = controller;
        _groundMask = groundMask;
    }

    /// <summary>World position of the bottom of the hull.</summary>
    public Vector3 Feet => _controller.transform.TransformPoint(_controller.center) - Vector3.up * (_controller.height / 2f);

    public float Height => _controller.height;

    /// <summary>Call from <c>OnControllerColliderHit</c> on the CharacterController's GameObject.</summary>
    public void OnHit(ControllerColliderHit hit)
    {
        _hitNormals?.Add(hit.normal);
    }

    public Vector3 Move(Vector3 displacement, List<Vector3> hitNormals)
    {
        Vector3 before = _controller.transform.position;
        _hitNormals = hitNormals;
        _controller.Move(displacement);
        _hitNormals = null;
        return _controller.transform.position - before;
    }

    public bool ProbeGround(float distance, float minNormalY, out Vector3 normal)
    {
        bool found = CastGround(distance, minNormalY, out float hover, out normal);
        Debug.DrawRay(Feet, Vector3.down * distance, found && hover <= distance ? Color.cyan : Color.magenta);
        if (found && hover <= distance)
            return true;

        normal = Vector3.up;
        return false;
    }

    public void StayOnGround(float maxDrop, float minNormalY)
    {
        if (CastGround(maxDrop, minNormalY, out float hover, out _) && hover > 1e-4f && hover <= maxDrop)
            _controller.Move(Vector3.down * hover);
    }

    public bool HasRoomForHeight(float height, bool keepTop)
    {
        float growth = height - _controller.height;
        if (growth <= 0f)
            return true;

        Vector3 feet = Feet + (keepTop ? Vector3.down * growth : Vector3.zero);
        float radius = _controller.radius - 0.01f;
        // Lifted clear of the ground the hull stands on
        Vector3 bottom = feet + Vector3.up * (_controller.radius + _controller.skinWidth);
        Vector3 top = feet + Vector3.up * (height - _controller.radius);
        return !Physics.CheckCapsule(bottom, top, radius, _groundMask, QueryTriggerInteraction.Ignore);
    }

    public bool TrySetHeight(float height, bool keepTop)
    {
        if (!HasRoomForHeight(height, keepTop))
            return false;

        float growth = height - _controller.height;
        _controller.height = height;
        _controller.center = Vector3.up * (height / 2f);

        if (keepTop && growth != 0f)
        {
            _controller.transform.position += Vector3.down * growth;
            Physics.SyncTransforms();
        }

        return true;
    }

    /// <summary>
    /// Sphere-casts the bottom of the hull down. <paramref name="hover"/> is how far it is above where it would rest,
    /// <paramref name="normal"/> the ground's. A contact too steep to stand on can be the rounded bottom touching an
    /// edge: then the face under the edge decides, so the pawn stands on a ledge's lip as HL2's box hull does instead
    /// of rolling off it.
    /// </summary>
    private bool CastGround(float extraDistance, float minNormalY, out float hover, out Vector3 normal)
    {
        hover = float.PositiveInfinity;
        normal = Vector3.up;

        float radius = _controller.radius;
        // At rest the hull floats SkinWidth off the ground, measured along the contact normal.
        float restGap = _controller.skinWidth + ProbeShrink;
        Vector3 feet = Feet;
        Vector3 origin = feet + Vector3.up * radius;

        if (!Physics.SphereCast(origin, radius - ProbeShrink, Vector3.down, out RaycastHit hit,
                restGap / MinContactNormalY + extraDistance, _groundMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        float contactY = Mathf.Max(hit.normal.y, MinContactNormalY);
        hover = hit.distance - restGap / contactY;
        normal = hit.normal;

        if (normal.y >= minNormalY)
            return true;

        // Look straight down just inside the edge, towards the hull's axis
        Vector3 inward = feet - hit.point;
        inward.y = 0f;
        Vector3 rayOrigin = hit.point + inward.normalized * 0.01f + Vector3.up * 0.05f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit face, 0.1f, _groundMask, QueryTriggerInteraction.Ignore)
            && face.normal.y >= minNormalY)
        {
            normal = face.normal;
            return true;
        }

        return false;
    }
}
