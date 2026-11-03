using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The primary In-Game HUD shown during active gameplay.
/// </summary>
public sealed class MainView : View
{
    [Header("Main stats")]
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text experienceText;

    [Header("Experience flash")]
    [Tooltip("The XP text's underlay color right after an experience pick-up.")]
    [SerializeField] private Color experienceFlashColor = Color.green;
    [Tooltip("How long the underlay takes to fade away after a pick-up.")]
    [SerializeField] private float experienceFlashDuration = 0.5f;
    [Tooltip("How far the underlay spreads past the glyphs (-1 to 1).")]
    [SerializeField] private float experienceFlashDilate = 1f;
    [Tooltip("How blurry the underlay's edge is (0 to 1).")]
    [SerializeField] private float experienceFlashSoftness = 0.5f;

    [Header("Overlays")]
    [SerializeField] private Image damageImage;
    [Tooltip("A full-screen Image with the Heal Border material: a soft green border around the screen's edge.")]
    [SerializeField] private Image healImage;

    [Header("Heal border")]
    [Tooltip("How wide the heal border gets per health healed, as a fraction of the screen's height.")]
    [SerializeField] private float healBorderWidthPerHealth = 0.025f;
    [Tooltip("The widest the heal border gets, as a fraction of the screen's height.")]
    [SerializeField] private float healBorderMaxWidth = 0.2f;
    [Tooltip("How long the heal border takes to fade away after a heal.")]
    [SerializeField] private float healBorderFadeDuration = 0.4f;

    [Header("Weapons")]
    [SerializeField] private TMP_Text primaryWeaponText;
    [SerializeField] private TMP_Text secondaryWeaponText;

    [Header("Ammo Texts")]
    [SerializeField] private TMP_Text bulletText;
    [SerializeField] private TMP_Text shotgunText;
    [SerializeField] private TMP_Text boltText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text explosiveText;
    [Tooltip("The ammo text of the weapon in hand is this many times its usual size.")]
    [SerializeField] private float heldAmmoFontScale = 1.4f;

    [Header("Interaction")]
    [Tooltip("Shows what pressing Interact would do, while a chest or weapon is in reach.")]
    [SerializeField] private TMP_Text interactPromptText;

    private static readonly WaitForSeconds waitForSeconds0_2 = new(0.2f);

    private int _shownExperience;
    private Coroutine _experienceFlash;
    private Coroutine _healFlash;
    private float _healBorderWidth; // the border's width right now, fading to 0
    private readonly Dictionary<TMP_Text, float> _ammoFontSizes = new(); // each ammo text's size in the prefab

    private void Awake()
    {
        // fontMaterial is a per-text instance, so the shared font material keeps no underlay.
        Material material = experienceText.fontMaterial;
        material.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        material.SetFloat(ShaderUtilities.ID_UnderlayDilate, experienceFlashDilate);
        material.SetFloat(ShaderUtilities.ID_UnderlaySoftness, experienceFlashSoftness);
        SetExperienceUnderlayAlpha(0f);

        foreach (AmmoType type in System.Enum.GetValues(typeof(AmmoType)))
        {
            TMP_Text text = GetAmmoText(type);
            if (text != null)
                _ammoFontSizes[text] = text.fontSize;
        }
    }

    public override void Show()
    {
        base.Show();
        Cursor.lockState = CursorLockMode.Locked;
        damageImage.gameObject.SetActive(false);
        healImage.gameObject.SetActive(false);
        _healFlash = null; // Hide stopped it with the view
        _healBorderWidth = 0f;
        HandleInteractTargetChanged(null);

        NetworkUIEvents.OnLocalHealthChanged += HandleHealthUpdate;
        NetworkUIEvents.OnLocalDamageTaken += HandleDamageTaken;
        NetworkUIEvents.OnLocalHeal += HandleHeal;
        NetworkUIEvents.OnLocalAmmoChanged += HandleAmmoChanged;
        NetworkUIEvents.OnLocalExperienceChanged += HandleExperienceChanged;
        NetworkUIEvents.OnLocalInteractTargetChanged += HandleInteractTargetChanged;
        NetworkUIEvents.OnLocalWeaponsChanged += HandleWeaponsChanged;

        // Events fired while hidden were missed; read the current counts.
        NetworkPlayer player = NetworkPlayer.LocalInstance;
        if (player != null)
        {
            foreach (AmmoType type in System.Enum.GetValues(typeof(AmmoType)))
                HandleAmmoChanged(type, player.GetAmmo(type));
            SetExperience(player.Experience); // no flash for pick-ups made while hidden

            Pawn pawn = player.ControlledPawn.Value;
            HandleWeaponsChanged(pawn != null ? pawn.PrimaryWeapon : null, pawn != null ? pawn.StowedWeapon : null);
        }
    }

    public override void Hide()
    {
        StopAllCoroutines();
        _experienceFlash = null;
        SetExperienceUnderlayAlpha(0f);

        NetworkUIEvents.OnLocalHealthChanged -= HandleHealthUpdate;
        NetworkUIEvents.OnLocalDamageTaken -= HandleDamageTaken;
        NetworkUIEvents.OnLocalHeal -= HandleHeal;
        NetworkUIEvents.OnLocalAmmoChanged -= HandleAmmoChanged;
        NetworkUIEvents.OnLocalExperienceChanged -= HandleExperienceChanged;
        NetworkUIEvents.OnLocalInteractTargetChanged -= HandleInteractTargetChanged;
        NetworkUIEvents.OnLocalWeaponsChanged -= HandleWeaponsChanged;

        base.Hide();
    }

    private void HandleHealthUpdate(int health)
    {
        healthText.text = health.ToString();
    }

    private void HandleAmmoChanged(AmmoType type, int count)
    {
        TMP_Text text = GetAmmoText(type);
        if (text != null)
            text.text = $"{type}: {count}";
    }

    private void HandleWeaponsChanged(Weapon held, Weapon stowed)
    {
        primaryWeaponText.text = held != null ? DisplayName(held.gameObject) : "";
        secondaryWeaponText.text = stowed != null ? DisplayName(stowed.gameObject) : "";

        foreach (var (text, fontSize) in _ammoFontSizes)
            text.fontSize = fontSize;
        TMP_Text heldAmmoText = held != null ? GetAmmoText(held.ammoType) : null;
        if (heldAmmoText != null)
            heldAmmoText.fontSize = _ammoFontSizes[heldAmmoText] * heldAmmoFontScale;
    }

    private void HandleInteractTargetChanged(Component target)
    {
        string prompt = target != null ? InteractPrompt(target) : null;

        interactPromptText.gameObject.SetActive(prompt != null);
        interactPromptText.text = prompt ?? "";
    }

    /// <summary>"[G] Pick up Machinegun", "[G] Open Weapon Chest": the target is named by its GameObject.</summary>
    private static string InteractPrompt(Component target)
    {
        string key = InputManager.PawnControls.Interact.GetBindingDisplayString(
            InputBinding.MaskByGroup("Keyboard&Mouse"));
        string verb = target is Weapon ? "Pick up" : "Open";
        return $"[{key}] {verb} {DisplayName(target.gameObject)}";
    }

    /// <summary>A GameObject's name without what Unity appends: "(Clone)" on instances, " (1)" on duplicates.</summary>
    private static string DisplayName(GameObject gameObject) =>
        Regex.Replace(gameObject.name, @"\(Clone\)|\s\(\d+\)$", "").Trim();

    private void HandleExperienceChanged(int experience)
    {
        if (experience > _shownExperience)
        {
            if (_experienceFlash != null)
                StopCoroutine(_experienceFlash);
            _experienceFlash = StartCoroutine(FlashExperience());
        }

        SetExperience(experience);
    }

    private void SetExperience(int experience)
    {
        _shownExperience = experience;
        experienceText.text = $"XP: {experience}";
    }

    private TMP_Text GetAmmoText(AmmoType type) => type switch
    {
        AmmoType.Bullet => bulletText,
        AmmoType.Shell => shotgunText,
        AmmoType.Bolt => boltText,
        AmmoType.Explosive => explosiveText,
        AmmoType.Energy => energyText,
        _ => null,
    };

    private void HandleDamageTaken(int damage)
    {
        StartCoroutine(DisplayDamage());
    }

    private void HandleHeal(int amount)
    {
        // Bigger heals show a wider border; a small one during a bigger one's fade doesn't shrink it.
        float width = Mathf.Min(amount * healBorderWidthPerHealth, healBorderMaxWidth);
        _healBorderWidth = Mathf.Max(_healBorderWidth, width);

        if (_healFlash != null)
            StopCoroutine(_healFlash);
        _healFlash = StartCoroutine(FlashHealBorder(_healBorderWidth));
    }

    private IEnumerator DisplayDamage()
    {
        damageImage.gameObject.SetActive(true);
        yield return waitForSeconds0_2;
        damageImage.gameObject.SetActive(false);
    }

    private IEnumerator FlashExperience()
    {
        // Full color at once, then fade out.
        for (float elapsed = 0f; elapsed < experienceFlashDuration; elapsed += Time.deltaTime)
        {
            SetExperienceUnderlayAlpha(1f - elapsed / experienceFlashDuration);
            yield return null;
        }

        SetExperienceUnderlayAlpha(0f);
        _experienceFlash = null;
    }

    private void SetExperienceUnderlayAlpha(float alpha)
    {
        Color color = experienceFlashColor;
        color.a *= alpha;
        experienceText.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, color);
    }

    private IEnumerator FlashHealBorder(float startWidth)
    {
        // material is a per-image instance, so the shared Heal Border material stays as authored.
        Material material = healImage.material;
        Rect rect = healImage.rectTransform.rect;
        material.SetFloat(HealBorderAspect, rect.height > 0f ? rect.width / rect.height : 1f);
        healImage.gameObject.SetActive(true);

        // Full width at once, then narrow and fade out together.
        for (float elapsed = 0f; elapsed < healBorderFadeDuration; elapsed += Time.deltaTime)
        {
            float remaining = 1f - elapsed / healBorderFadeDuration;
            _healBorderWidth = startWidth * remaining;
            material.SetFloat(HealBorderWidth, _healBorderWidth);
            SetHealImageAlpha(remaining);
            yield return null;
        }

        _healBorderWidth = 0f;
        healImage.gameObject.SetActive(false);
        _healFlash = null;
    }

    private void SetHealImageAlpha(float alpha)
    {
        Color color = healImage.color;
        color.a = alpha;
        healImage.color = color;
    }

    private static readonly int HealBorderWidth = Shader.PropertyToID("_Width");
    private static readonly int HealBorderAspect = Shader.PropertyToID("_Aspect");
}