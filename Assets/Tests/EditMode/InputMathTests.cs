using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit-mode unit tests for HumanInputMath.
/// These run instantly in the Unity Test Runner - no Play Mode, no networking, no GameObjects.
/// </summary>
[TestFixture]
public class InputMathTests
{
    private const float Tolerance = 0.0001f;

    [Test]
    public void ApplyLookSensitivity_ScalesByMultiplier_NonWebGL()
    {
        Vector2 raw = new Vector2(3f, -2f);
        Vector2 result = HumanInputMath.ApplyLookSensitivity(raw, 2f, isWebGL: false);

        Assert.AreEqual(6f, result.x, Tolerance,
            "Look X should equal raw X times sensitivity.");
        Assert.AreEqual(-4f, result.y, Tolerance,
            "Look Y should equal raw Y times sensitivity.");
    }

    [Test]
    public void ApplyLookSensitivity_SensitivityOne_IsIdentity_NonWebGL()
    {
        Vector2 raw = new Vector2(1.5f, 2.5f);
        Vector2 result = HumanInputMath.ApplyLookSensitivity(raw, 1f, isWebGL: false);

        Assert.AreEqual(raw.x, result.x, Tolerance);
        Assert.AreEqual(raw.y, result.y, Tolerance);
    }

    [Test]
    public void ApplyLookSensitivity_WebGL_DividesByExpectedFactor()
    {
        Vector2 raw = new Vector2(100f, -50f);
        Vector2 result = HumanInputMath.ApplyLookSensitivity(raw, 1f, isWebGL: true);

        // raw * 1f / 10f
        Assert.AreEqual(10f, result.x, Tolerance,
            "WebGL look X should be raw X divided by the WebGL divisor.");
        Assert.AreEqual(-5f, result.y, Tolerance,
            "WebGL look Y should be raw Y divided by the WebGL divisor.");
    }

    [Test]
    public void ApplyLookSensitivity_WebGL_AppliesSensitivityBeforeDivisor()
    {
        Vector2 raw = new Vector2(20f, 0f);
        Vector2 result = HumanInputMath.ApplyLookSensitivity(raw, 5f, isWebGL: true);

        // raw * 5 / 10 = raw * 0.5
        Assert.AreEqual(10f, result.x, Tolerance);
    }

    [Test]
    public void ApplyLookSensitivity_WebGL_ProducesDifferentResultThanNonWebGL()
    {
        Vector2 raw = new Vector2(7f, 11f);
        const float sens = 1.3f;

        Vector2 nonWeb = HumanInputMath.ApplyLookSensitivity(raw, sens, isWebGL: false);
        Vector2 web = HumanInputMath.ApplyLookSensitivity(raw, sens, isWebGL: true);

        Assert.AreNotEqual(nonWeb, web,
            "WebGL branch should produce a different value than the standalone branch.");
        Assert.AreEqual(nonWeb / HumanInputMath.WebGLLookDivisor, web,
            "WebGL result should equal non-WebGL result divided by the WebGL divisor.");
    }

    [Test]
    public void ApplyLookSensitivity_ZeroInput_ReturnsZero_NonWebGL()
    {
        Vector2 result = HumanInputMath.ApplyLookSensitivity(Vector2.zero, 3f, isWebGL: false);

        Assert.AreEqual(Vector2.zero, result,
            "Zero raw input must produce zero output regardless of sensitivity.");
    }

    [Test]
    public void ApplyLookSensitivity_ZeroInput_ReturnsZero_WebGL()
    {
        Vector2 result = HumanInputMath.ApplyLookSensitivity(Vector2.zero, 3f, isWebGL: true);

        Assert.AreEqual(Vector2.zero, result,
            "Zero raw input must produce zero output on WebGL as well.");
    }

    [Test]
    public void ApplyLookSensitivity_ZeroSensitivity_ReturnsZero()
    {
        Vector2 raw = new Vector2(42f, -17f);
        Vector2 nonWeb = HumanInputMath.ApplyLookSensitivity(raw, 0f, isWebGL: false);
        Vector2 web = HumanInputMath.ApplyLookSensitivity(raw, 0f, isWebGL: true);

        Assert.AreEqual(Vector2.zero, nonWeb);
        Assert.AreEqual(Vector2.zero, web);
    }

    [Test]
    public void ApplyLookSensitivity_NegativeSensitivity_InvertsLook()
    {
        Vector2 raw = new Vector2(4f, 6f);
        Vector2 result = HumanInputMath.ApplyLookSensitivity(raw, -1f, isWebGL: false);

        Assert.AreEqual(-4f, result.x, Tolerance);
        Assert.AreEqual(-6f, result.y, Tolerance);
    }
}
