using UnityEngine;

/// <summary>
/// Procedural firing animation and muzzle flash sizing, so weapons don't each need hand-made fire clips.
/// Each shot snaps the model back, rises its muzzle, and eases it back to rest; how hard comes from
/// <see cref="WeaponKickSettings"/>. A shot fired mid-kick starts from wherever the last one left the model.
/// Only plays on weapons without an Animator: those use their hand-made fire clips instead.
/// </summary>
[RequireComponent(typeof(Weapon))]
public class WeaponKick : MonoBehaviour
{
    [SerializeField] private WeaponKickSettings settings;

    [Tooltip("The visual model the kick moves. Keep the muzzle and spawn points out of it, so aim is unaffected.")]
    [SerializeField] private Transform model;

    [Tooltip("Scales this weapon's raw kick, for weapons the formula gets wrong.")]
    [Min(0)]
    [SerializeField] private float strengthMultiplier = 1f;

    private Weapon _weapon;
    private float _size; // diagonal of the model's bounding box, in the weapon's space

    private ParticleSystem _flash;
    private float _flashBaseSize;
    private float _flashBaseSpeed;

    private bool _isKicking;
    private float _elapsed;
    private Vector3 _fromPosition, _peakPosition; // offsets from the model's rest pose
    private float _fromPitch, _fromYaw, _peakYaw;
    private float _side = 1f; // sign of the last shot's sideways shift; shots alternate sides
    private KickProfile _profile;

    private Vector3 _restPosition;
    private Quaternion _restRotation;
    private Vector3 _offsetPosition;
    private Vector2 _offsetAngles; // pitch (positive dips the muzzle), yaw

    /// <summary>This weapon's kick as of its last shot (or as first worked out), for inspecting the tuning.</summary>
    public KickProfile Profile => _profile;

#if UNITY_EDITOR
    // Wires up a freshly added kick: the shared settings, the usual model child, and the weapon's reference to it.
    private void Reset()
    {
        string[] found = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(WeaponKickSettings));
        if (found.Length > 0)
            settings = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponKickSettings>(UnityEditor.AssetDatabase.GUIDToAssetPath(found[0]));
        model = transform.Find("Weapon Model");
        GetComponent<Weapon>().kick = this;
    }
#endif

    private void Awake()
    {
        _weapon = GetComponent<Weapon>();
        _size = MeasureSize();
        _profile = settings.Evaluate(_weapon, _size, strengthMultiplier);

        _flash = _weapon.muzzleFlash;
        if (_flash != null)
        {
            ParticleSystem.MainModule main = _flash.main;
            _flashBaseSize = main.startSizeMultiplier;
            _flashBaseSpeed = main.startSpeedMultiplier;
        }

        _restPosition = model.localPosition;
        _restRotation = model.localRotation;
    }

    /// <summary>
    /// Kicks the model and sizes the muzzle flash for one shot. Call before the flash plays.
    /// </summary>
    public void Play()
    {
        // Worked out per shot, in case the weapon's stats change while it's held.
        _profile = settings.Evaluate(_weapon, _size, strengthMultiplier);
        float randomness = _profile.Randomness;
        float jitter = 1f + Random.Range(-1f, 1f) * randomness * _profile.StrengthJitter;

        // Shots alternate sides by a random amount, so continuous fire never repeats the same motion.
        if (randomness > 0)
            _side = -Mathf.Sign(_side) * Random.Range(0.25f, 1f);

        _fromPosition = _offsetPosition;
        _fromPitch = _offsetAngles.x;
        _fromYaw = _offsetAngles.y;
        _peakPosition = new Vector3(
            _profile.MaxLateralOffset * randomness * _side,
            _profile.VerticalOffset * jitter,
            -_profile.BackDistance * jitter);
        _peakYaw = _profile.MaxYaw * randomness * Random.Range(-1f, 1f);
        _profile.Pitch *= jitter;
        _profile.Dip *= jitter;
        _elapsed = 0;
        _isKicking = true;

        if (_flash != null)
        {
            float scale = _profile.FlashScale * (1f + Random.Range(-1f, 1f) * randomness * _profile.FlashJitter);
            ParticleSystem.MainModule main = _flash.main;
            main.startSizeMultiplier = _flashBaseSize * scale;
            main.startSpeedMultiplier = _flashBaseSpeed * scale;
        }
    }

    private void LateUpdate()
    {
        if (!_isKicking)
            return;

        _elapsed += Time.deltaTime;
        if (_elapsed >= _profile.Duration)
        {
            _isKicking = false;
            _offsetPosition = Vector3.zero;
            _offsetAngles = Vector2.zero;
        }
        else
        {
            EvaluateOffsets(_elapsed);
        }

        model.SetLocalPositionAndRotation(
            _restPosition + _offsetPosition,
            _restRotation * Quaternion.Euler(_offsetAngles.x, _offsetAngles.y, 0));
    }

    /// <summary>
    /// The kick's shape over time, after the Test Uzi and Test Shotgun clips: the model snaps back and the
    /// muzzle dips, the pose holds, the muzzle rises to its peak, then everything eases back to rest.
    /// </summary>
    private void EvaluateOffsets(float t)
    {
        KickProfile p = _profile;

        // Snap from wherever the model was to the kicked pose, with the muzzle dipped.
        if (t < p.AttackTime)
        {
            float snap = Mathf.SmoothStep(0, 1, t / p.AttackTime);
            _offsetPosition = Vector3.Lerp(_fromPosition, _peakPosition, snap);
            _offsetAngles = new Vector2(Mathf.Lerp(_fromPitch, p.Dip, snap), Mathf.Lerp(_fromYaw, _peakYaw, snap));
            return;
        }

        // Position and yaw hold, then ease back.
        float settleStart = p.AttackTime + p.HoldTime;
        float shift = Mathf.SmoothStep(1, 0, Mathf.InverseLerp(settleStart, p.Duration, t));
        _offsetPosition = _peakPosition * shift;
        _offsetAngles.y = _peakYaw * shift;

        // Pitch holds the dip, rises to its peak, then eases back.
        if (t < settleStart)
            _offsetAngles.x = p.Dip;
        else if (t < p.PitchPeakTime)
            _offsetAngles.x = Mathf.SmoothStep(p.Dip, -p.Pitch, Mathf.InverseLerp(settleStart, p.PitchPeakTime, t));
        else
            _offsetAngles.x = Mathf.SmoothStep(-p.Pitch, 0, Mathf.InverseLerp(p.PitchPeakTime, p.Duration, t));
    }

    /// <summary>Diagonal of the bounding box of every mesh under the model, measured in the weapon's own space.</summary>
    private float MeasureSize()
    {
        Bounds bounds = default;
        bool found = false;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null)
                continue;

            Matrix4x4 toWeapon = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Bounds mesh = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mesh.center + Vector3.Scale(mesh.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = toWeapon.MultiplyPoint3x4(corner);
                if (found)
                    bounds.Encapsulate(point);
                else
                    bounds = new Bounds(point, Vector3.zero);
                found = true;
            }
        }
        return found ? bounds.size.magnitude : 1f;
    }
}
