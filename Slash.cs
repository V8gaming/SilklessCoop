using UnityEngine;
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SilklessCoop
{
    public class Slash : NailSlash
    {
        // This class inherits all fields and properties from NailSlash
        // We only override the methods that need to remove HeroController dependencies

        public new void StartSlash()
        {
            try
            {
                var startTime = UnityEngine.Time.realtimeSinceStartup;
                UnityEngine.Debug.Log($"[SLASH PERF] Starting slash on {gameObject.name} at {startTime:F3}s");

                // Initialize slash without HeroController dependency
                this.OnSlashStarting();

                // Set audio pitch
                float num = this.originalPitch;
                // Note: Removed quickening check since we don't have HeroController

                // Play audio
                if (this.audio != null)
                {
                    this.audio.pitch = num;
                    this.audio.Play();
                    UnityEngine.Debug.Log($"[SLASH PERF] Audio played for {gameObject.name}");
                }

                // Reset animation and state
                this.animTriggerCounter = 0;
                this.enabled = false;
                this.queuedDownspikeBounce = false;

                // Activate damager
                this.IsDamagerActive = true;
                this.IsStartingSlash = true;

                // Play slash effect
                var animTime = UnityEngine.Time.realtimeSinceStartup;
                this.PlaySlash();
                UnityEngine.Debug.Log($"[SLASH PERF] PlaySlash took {(UnityEngine.Time.realtimeSinceStartup - animTime)*1000:F2}ms");

                // Skip drill dash - movement/transform data is handled separately by the sync system

                UnityEngine.Debug.Log($"[SLASH PERF] StartSlash completed in {(UnityEngine.Time.realtimeSinceStartup - startTime)*1000:F2}ms");
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[SLASH PERF] Error in StartSlash: {e.Message}");
                UnityEngine.Debug.LogError($"[SLASH PERF] Stack trace: {e.StackTrace}");
            }
        }

        private new void PlaySlash()
        {
            // Enable mesh if available
            if (this.mesh != null)
            {
                this.mesh.enabled = true;
            }

            // Set up animation events
            if (this.anim != null)
            {
                this.anim.AnimationEventTriggered = new Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>(this.OnAnimationEventTriggered);
                this.anim.AnimationCompleted = new Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>(this.OnAnimationCompleted);

                // Set animation speed (removed quickening dependency)
                float speedMultiplier = 1f; // Default speed since we don't have HeroController

                // Play the animation - check if animName exists first
                if (!string.IsNullOrEmpty(this.animName))
                {
                    UnityEngine.Debug.Log($"[SLASH PERF] Trying to play animation: '{this.animName}' on {gameObject.name}");
                    tk2dSpriteAnimationClip clipByName = this.anim.GetClipByName(this.animName);
                    if (clipByName != null)
                    {
                        this.anim.Play(clipByName, Mathf.Epsilon, clipByName.fps * speedMultiplier);
                        UnityEngine.Debug.Log($"[SLASH PERF] Successfully started animation: '{this.animName}'");
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"[SLASH PERF] Animation clip '{this.animName}' not found in animator on {gameObject.name}");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError($"[SLASH PERF] animName is null or empty on {gameObject.name} - cannot play animation");

                    // Try to get a default animation name from the animator
                    if (this.anim.Library != null && this.anim.Library.clips.Length > 0)
                    {
                        var defaultClip = this.anim.Library.clips[0];
                        if (defaultClip != null)
                        {
                            UnityEngine.Debug.Log($"[SLASH PERF] Using first available clip as fallback: '{defaultClip.name}'");
                            this.anim.Play(defaultClip, Mathf.Epsilon, defaultClip.fps * speedMultiplier);
                        }
                    }
                }
            }

            // Call base method
            this.OnPlaySlash();
        }

        public new void OnPlaySlash()
        {
            // Use the original base implementation from NailAttackBase
            base.OnPlaySlash();
        }

        // Configure slash component with data from local player's HeroController
        public void ConfigureFromHeroControllerData(string attackDirection, string playerDataInfo, string slashComponentInfo, string damagerInfo)
        {
            try
            {
                // Store the attack direction for reference
                var traverse = Traverse.Create(this);
                traverse.Field("currentAttack").SetValue(attackDirection);

                // Apply player data configurations (CurrentCrestID affects attack behavior)
                if (playerDataInfo != "null" && int.TryParse(playerDataInfo, out int crestID))
                {
                    // Store crest ID for potential use in attack modifications
                    // Different crests may affect damage, effects, or animations
                    traverse.Field("configuredCrestID").SetValue(crestID);
                }

                // Configure based on slash component type
                if (slashComponentInfo != "null")
                {
                    // Different slash component types may have different behaviors
                    // Store the component type for reference
                    traverse.Field("configuredSlashType").SetValue(slashComponentInfo);
                }

                // Configure based on damager information
                if (damagerInfo != "null")
                {
                    // Store damager type information
                    traverse.Field("configuredDamagerType").SetValue(damagerInfo);
                }

                // Log configuration
                if (this.gameObject.name.Contains("SilklessCooperator"))
                {
                    Debug.Log($"Configured Slash on {this.gameObject.name} - Direction: {attackDirection}, Crest: {playerDataInfo}, SlashType: {slashComponentInfo}, Damager: {damagerInfo}");
                }
            }
            catch (Exception configEx)
            {
                Debug.LogError($"Error configuring Slash component: {configEx}");
            }
        }

        // Event methods (implement as needed)
        protected new virtual void OnSlashStarting() { }
        protected new virtual void OnAnimationEventTriggered(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip, int frameIdx) { }
        protected new virtual void OnAnimationCompleted(tk2dSpriteAnimator animator, tk2dSpriteAnimationClip clip) { }
    }
}