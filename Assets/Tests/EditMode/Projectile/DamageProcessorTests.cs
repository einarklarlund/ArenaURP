using Arena.Damage;
using NUnit.Framework;

namespace Arena.Tests.Damage
{
    /// <summary>EditMode tests for the pure Damage resolution seam (base + multiplier, never negative).</summary>
    public class DamageProcessorTests
    {
        [Test]
        public void Resolve_IdentityMultiplier_NeverNegative()
        {
            Assert.AreEqual(7, DamageProcessor.Resolve(7, 1f));
            Assert.AreEqual(0, DamageProcessor.Resolve(-3, 1f));
        }

        [Test]
        public void Resolve_Multiplier_RoundsAndClamps()
        {
            Assert.AreEqual(15, DamageProcessor.Resolve(10, 1.5f));
            Assert.AreEqual(0, DamageProcessor.Resolve(10, -1f));
        }
    }
}
