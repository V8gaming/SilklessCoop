using UnityEngine;

namespace SilklessCoop
{
    // Dummy HeroController for remote players
    public class DummyController : MonoBehaviour
    {
        // Attack state
        public string currentAttackDirection = "normal";
        public bool isUpAttacking = false;
        public bool isDownAttacking = false;

        // Equipment state
        public bool hasLongNeedle = false;

        // Crest/Nail imbuement
        public string crestName = null;

        // Future expansion
        public bool isUsingQuickening = false;

        // Helper method to update attack state from attack data
        public void UpdateFromAttackData(string attackDirection, bool longNeedleEquipped, string crest)
        {
            currentAttackDirection = attackDirection;
            hasLongNeedle = longNeedleEquipped;
            crestName = crest;

            // Set attack direction flags
            isUpAttacking = attackDirection?.ToLower() == "up";
            isDownAttacking = attackDirection?.ToLower() == "down";
        }
    }
}