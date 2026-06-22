using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The primary In-Game HUD shown during active gameplay.
/// </summary>
public sealed class MainView : View
{
    [Header("Main stats")]
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text temperatureText;

    [Header("Overlays")]
    [SerializeField] private Image damageImage;

    [Header("Weapons")]
    [SerializeField] private TMP_Text primaryWeaponText;
    [SerializeField] private TMP_Text secondaryWeaponText;

    [Header("Ammo Texts")]
    [SerializeField] private TMP_Text bulletText;
    [SerializeField] private TMP_Text shotgunText;
    [SerializeField] private TMP_Text boltText;
    [SerializeField] private TMP_Text energyText;
    [SerializeField] private TMP_Text explosiveText;

    private static readonly WaitForSeconds waitForSeconds0_2 = new(0.2f);

    public override void Show()
    {
        base.Show();
        Cursor.lockState = CursorLockMode.Locked;
        damageImage.gameObject.SetActive(false);

        NetworkUIEvents.OnLocalHealthChanged += HandleHealthUpdate;
        LocalUIEvents.OnTemperatureChanged += HandleTemperatureUpdate;
        NetworkUIEvents.OnLocalDamageTaken += HandleDamageTaken;
    }

    public override void Hide()
    {
        StopAllCoroutines();

        NetworkUIEvents.OnLocalHealthChanged -= HandleHealthUpdate;
        LocalUIEvents.OnTemperatureChanged -= HandleTemperatureUpdate;
        // This makes it so that a player can be damaged while the main view isn't shown and
        // their health isn't updated. I'll have to fix it later.
        NetworkUIEvents.OnLocalDamageTaken -= HandleDamageTaken;

        base.Hide();
    }

    private void HandleHealthUpdate(int health)
    {
        healthText.text = health.ToString();
    }

    private void HandleTemperatureUpdate(float temperature)
    {
        temperatureText.text = temperature.ToString("F2");
    }

    private void HandleDamageTaken(int damage)
    {
        StartCoroutine(DisplayDamage());
    }

    private IEnumerator DisplayDamage()
    {
        damageImage.gameObject.SetActive(true);
        yield return waitForSeconds0_2;
        damageImage.gameObject.SetActive(false);
    }
}