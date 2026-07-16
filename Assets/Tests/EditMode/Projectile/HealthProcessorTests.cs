using Arena.Health;
using NUnit.Framework;

namespace Arena.Tests.Health
{
    /// <summary>EditMode tests for the pure Health arithmetic (clamp-correct damage/heal + death).</summary>
    public class HealthProcessorTests
    {
        [Test]
        public void ApplyDamage_ClampsAtZero()
        {
            var s = HealthState.Full(5);
            Assert.AreEqual(2, HealthProcessor.ApplyDamage(s, 3).Hp);
            Assert.AreEqual(0, HealthProcessor.ApplyDamage(s, 99).Hp);
        }

        [Test]
        public void Heal_ClampsAtMax()
        {
            var s = new HealthState { Hp = 2, MaxHp = 5 };
            Assert.AreEqual(4, HealthProcessor.Heal(s, 2).Hp);
            Assert.AreEqual(5, HealthProcessor.Heal(s, 99).Hp);
        }

        [Test]
        public void IsDead_TrueAtZero()
        {
            Assert.IsTrue(new HealthState { Hp = 0, MaxHp = 5 }.IsDead);
            Assert.IsFalse(HealthState.Full(1).IsDead);
        }

        [Test]
        public void Fraction_IsHpOverMax()
        {
            Assert.AreEqual(1f, HealthState.Full(20).Fraction, 1e-6f);
            Assert.AreEqual(0.5f, new HealthState { Hp = 5, MaxHp = 10 }.Fraction, 1e-6f);
            Assert.AreEqual(0f, new HealthState { Hp = 0, MaxHp = 10 }.Fraction, 1e-6f);
        }

        [Test]
        public void Fraction_ZeroMax_IsZero_NotDivideByZero()
        {
            Assert.AreEqual(0f, new HealthState { Hp = 5, MaxHp = 0 }.Fraction);
        }
    }
}
