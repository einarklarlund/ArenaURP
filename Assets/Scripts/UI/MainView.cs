using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The primary In-Game HUD (Heads-Up Display) shown during active gameplay.
/// Responsibilities:
/// - Acts as the visual interface for the player's real-time survival data.
/// - Reacts to health updates relayed through the NetworkUIEvents bus to update the UI efficiently.
/// - Completely decoupled from the Player and Pawn classes, relying solely on event-driven signals.
/// - Only remains active and visible while the local player is actively controlling a Pawn.
/// </summary>
public sealed class MainView : View
{
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private Image damageImage;
    [SerializeField] private TMP_Text primaryWeaponText;
    [SerializeField] private TMP_Text secondaryWeaponText;
    [Header("Ammo Texts")]
    [SerializeField] private TMP_Text bulletText;
    [SerializeField] private TMP_Text shotgunText;
    [SerializeField] private TMP_Text boltText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text explosiveText;

    private Dictionary<AmmoType, TMP_Text> ammoTexts;
    // [SerializeField] Slider fireSlider;

    public override void Show()
    {
        base.Show();
        Cursor.lockState = CursorLockMode.Locked;
        damageImage.gameObject.SetActive(false);
    }

    public override void Hide()
    {
        StopAllCoroutines();
        base.Hide();
    }

    private void Start()
    {
        NetworkUIEvents.OnLocalHealthChanged += HandleHealthUpdate;
        NetworkUIEvents.OnWeaponInventoryChanged += HandleWeaponInventoryChange;
        NetworkUIEvents.OnAmmoPoolChanged += HandleAmmoPoolChanged;
        NetworkUIEvents.OnLocalDamageTaken += HandleDamageTaken;

        ammoTexts = new()
        {
            { AmmoType.Bullet, bulletText },
            { AmmoType.Shell, bulletText },
            { AmmoType.Bolt, bulletText },
            { AmmoType.Energy, bulletText },
            { AmmoType.Explosive, bulletText }
        };
    }

    private void HandleHealthUpdate(int health)
    {
        healthText.text = health.ToString();
    }

    private void HandleDamageTaken(int damage)
    {
        StartCoroutine(DisplayDamage());
    }

    private IEnumerator DisplayDamage()
    {
        damageImage.gameObject.SetActive(true);
        yield return new WaitForSeconds(0.2f);
        damageImage.gameObject.SetActive(false);
    }

    private void HandleWeaponInventoryChange(int index, WeaponSlot slot)
    {
        string text = slot.IsOccupied ? WeaponDatabase.Instance.GetWeapon(slot.WeaponID).name : "";
        if (index == 0)
        {
            primaryWeaponText.text = text;
        }
        else
        {
            secondaryWeaponText.text = text;
        }
    }

    private void HandleAmmoPoolChanged(AmmoType ammoType, int numAmmo)
    {
        ammoTexts[ammoType].text = $"{ammoType}: {numAmmo}";
    }
}