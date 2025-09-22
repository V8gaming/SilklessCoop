using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using System.Reflection;
using System;

namespace SilklessCoop
{
    public static class AttackPatches
    {
        private static GameSync _gameSync;
        private static ManualLogSource _logger;

        public static void Initialize(GameSync gameSync, ManualLogSource logger)
        {
            _gameSync = gameSync;
            _logger = logger;
        }

        [HarmonyPatch]
        public static class HeroControllerPatch
        {
            [HarmonyTargetMethod]
            static System.Reflection.MethodBase TargetMethod()
            {
                var heroControllerType = System.Type.GetType("HeroController");
                if (heroControllerType == null)
                {
                    // Try with assembly qualified name
                    heroControllerType = AccessTools.TypeByName("HeroController");
                }
                return AccessTools.Method(heroControllerType, "Attack");
            }

            [HarmonyPrefix]
            static void AttackPrefix(object __instance, object __0)
            {
                if (_gameSync == null) return;

                var startTime = UnityEngine.Time.realtimeSinceStartup;

                try
                {
                    _logger?.LogInfo($"[LOCAL ATTACK PERF] Attack prefix started at {startTime:F3}s");

                    // Add currentAttack field and set it to attackDir
                    var traverse = Traverse.Create(__instance);
                    traverse.Field("currentAttack").SetValue(__0);

                    // Extract attack direction
                    string attackDirection = __0?.ToString() ?? "Unknown";
                    string paramType = __0?.GetType()?.Name ?? "null";

                    _logger?.LogInfo($"[LOCAL ATTACK PERF] Attack parameter type: {paramType}, value: {attackDirection}");
                    _logger?.LogInfo($"[LOCAL ATTACK PERF] __instance type: {__instance?.GetType()?.Name}");

                    // Extract additional HeroController properties
                    var heroTraverse = Traverse.Create(__instance);
                    string crestConfigStr = "null";
                    string slashComponentStr = "null";
                    bool hasLongNeedle = false;

                    try
                    {
                        // Extract crestConfig (private field) - get the name property
                        var crestConfig = heroTraverse.Field("crestConfig").GetValue();
                        if (crestConfig != null)
                        {
                            var crestTraverse = Traverse.Create(crestConfig);
                            var crestName = crestTraverse.Property("name").GetValue();
                            crestConfigStr = crestName?.ToString() ?? crestConfig.GetType().Name;
                        }

                        // Check if LongNeedle is equipped
                        var gameplayType = AccessTools.TypeByName("Gameplay");
                        if (gameplayType != null)
                        {
                            var longNeedleToolProperty = AccessTools.Property(gameplayType, "LongNeedleTool");
                            if (longNeedleToolProperty != null)
                            {
                                var longNeedleTool = longNeedleToolProperty.GetValue(null);
                                if (longNeedleTool != null)
                                {
                                    var isEquippedProperty = AccessTools.Property(longNeedleTool.GetType(), "IsEquipped");
                                    if (isEquippedProperty != null)
                                    {
                                        hasLongNeedle = (bool)isEquippedProperty.GetValue(longNeedleTool);
                                        _logger?.LogInfo($"LongNeedleTool equipped: {hasLongNeedle}");
                                    }
                                }
                            }
                        }

                        // Extract SlashComponent (property) - get the name property of the NailSlash object
                        var slashComponent = heroTraverse.Property("SlashComponent").GetValue();
                        if (slashComponent != null)
                        {
                            var slashTraverse = Traverse.Create(slashComponent);
                            var slashName = slashTraverse.Property("name").GetValue();
                            slashComponentStr = slashName?.ToString() ?? slashComponent.GetType().Name;
                            _logger?.LogInfo($"SlashComponent details - Type: {slashComponent.GetType().Name}, Name: {slashComponentStr}");
                        }

                        // Map attack direction to expected slash type
                        // Since SlashComponent shows the last attack, we can determine the current one from direction
                        if (attackDirection != null && attackDirection != "Unknown")
                        {
                            _logger?.LogInfo($"Attack direction indicates: {attackDirection}");
                            // Override with expected slash based on direction if needed
                            switch (attackDirection.ToLower())
                            {
                                case "normal":
                                    slashComponentStr = "Slash";
                                    break;
                                case "up":
                                    slashComponentStr = "UpSlash";
                                    break;
                                case "down":
                                    slashComponentStr = "DownSlash";
                                    break;
                                case "wall":
                                    slashComponentStr = "WallSlash";
                                    break;
                                default:
                                    // Keep the extracted value
                                    break;
                            }
                        }

                        _logger?.LogInfo($"Extracted HeroController data - CrestConfig: {crestConfigStr}, SlashComponent: {slashComponentStr}, LongNeedle: {hasLongNeedle}");
                    }
                    catch (System.Exception extractEx)
                    {
                        _logger?.LogError($"Error extracting HeroController properties: {extractEx}");
                    }

                    // Create composite attack data (now includes LongNeedle status)
                    var dataTime = UnityEngine.Time.realtimeSinceStartup;
                    string attackData = $"{attackDirection}|{crestConfigStr}|{slashComponentStr}|{(hasLongNeedle ? "1" : "0")}";
                    _logger?.LogInfo($"[LOCAL ATTACK PERF] Attack data creation took {(UnityEngine.Time.realtimeSinceStartup - dataTime)*1000:F2}ms");

                    // Only queue attack if we're connected to avoid connection spam
                    if (_gameSync.IsConnected())
                    {
                        var queueTime = UnityEngine.Time.realtimeSinceStartup;
                        _gameSync.SetCurrentAttack(attackData);
                        _logger?.LogInfo($"[LOCAL ATTACK PERF] SetCurrentAttack took {(UnityEngine.Time.realtimeSinceStartup - queueTime)*1000:F2}ms");
                        _logger?.LogInfo($"Set composite attack data and queued for network: {attackData}");
                    }
                    else
                    {
                        _logger?.LogInfo($"Attack not queued - not connected to server: {attackData}");
                    }

                    var totalTime = (UnityEngine.Time.realtimeSinceStartup - startTime) * 1000;
                    _logger?.LogInfo($"[LOCAL ATTACK PERF] Total attack prefix time: {totalTime:F2}ms");

                    if (totalTime > 5.0f) // More than 5ms might cause noticeable lag
                    {
                        _logger?.LogWarning($"[LOCAL ATTACK PERF] WARNING: Attack prefix took {totalTime:F2}ms - this could cause lag!");
                    }
                }
                catch (System.Exception e)
                {
                    _logger?.LogError($"[LOCAL ATTACK PERF] Error in Attack prefix: {e}");
                }
            }

        }

        // Extension method to add RemotePlaySlash to NailSlash
        public static void RemotePlaySlash(this NailSlash instance)
        {
            try
            {
                var traverse = Traverse.Create(instance);

                // Enable mesh if available
                var mesh = traverse.Field("mesh").GetValue() as MeshRenderer;
                if (mesh != null)
                {
                    mesh.enabled = true;
                }

                // Set up animation
                var anim = traverse.Field("anim").GetValue() as tk2dSpriteAnimator;
                if (anim != null)
                {
                    // Get the animation name
                    var animName = traverse.Field("animName").GetValue() as string;

                    // Set up animation event handlers
                    var animEventTriggeredMethod = AccessTools.Method(instance.GetType(), "OnAnimationEventTriggered");
                    var animCompletedMethod = AccessTools.Method(instance.GetType(), "OnAnimationCompleted");

                    if (animEventTriggeredMethod != null)
                    {
                        anim.AnimationEventTriggered = (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>)
                            Delegate.CreateDelegate(typeof(Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip, int>), instance, animEventTriggeredMethod);
                    }

                    if (animCompletedMethod != null)
                    {
                        anim.AnimationCompleted = (Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>)
                            Delegate.CreateDelegate(typeof(Action<tk2dSpriteAnimator, tk2dSpriteAnimationClip>), instance, animCompletedMethod);
                    }

                    // Use default speed (no HeroController quickening check)
                    float speedMultiplier = 1f;

                    tk2dSpriteAnimationClip clipByName = anim.GetClipByName(animName);
                    if (clipByName != null)
                    {
                        anim.Play(clipByName, Mathf.Epsilon, clipByName.fps * speedMultiplier);
                    }
                }

                // Call base OnPlaySlash method from NailAttackBase safely
                var baseOnPlaySlashMethod = AccessTools.Method(typeof(NailAttackBase), "OnPlaySlash");
                if (baseOnPlaySlashMethod != null)
                {
                    baseOnPlaySlashMethod.Invoke(instance, null);
                }

                _logger?.LogInfo($"RemotePlaySlash executed successfully for {instance.gameObject.name}");
            }
            catch (Exception e)
            {
                _logger?.LogError($"Error in RemotePlaySlash: {e}");
            }
        }

        // Extension method to add RemoteStartSlash to NailSlash
        public static void RemoteStartSlash(this NailSlash instance)
        {
            try
            {
                var traverse = Traverse.Create(instance);

                // Call OnSlashStarting
                var onSlashStartingMethod = AccessTools.Method(instance.GetType(), "OnSlashStarting");
                if (onSlashStartingMethod != null)
                {
                    onSlashStartingMethod.Invoke(instance, null);
                }

                // Set audio pitch (without HeroController quickening check)
                var originalPitch = traverse.Field("originalPitch").GetValue();
                var audio = traverse.Field("audio").GetValue() as AudioSource;
                if (audio != null && originalPitch != null)
                {
                    float num = (float)originalPitch;
                    // Use default pitch (no quickening)
                    audio.pitch = num;
                    audio.Play();
                }

                // Reset animation and state
                traverse.Field("animTriggerCounter").SetValue(0);
                traverse.Field("queuedDownspikeBounce").SetValue(false);

                // Get poly field and disable it
                var poly = traverse.Field("poly").GetValue() as PolygonCollider2D;
                if (poly != null)
                {
                    poly.enabled = false;
                }

                // Activate damager
                var isDamagerActiveProperty = traverse.Property("IsDamagerActive");
                if (isDamagerActiveProperty != null)
                {
                    isDamagerActiveProperty.SetValue(true);
                }

                traverse.Field("IsStartingSlash").SetValue(true);

                // Call PlaySlash (which will use RemotePlaySlash for remote players)
                instance.PlaySlash();

                // Skip drill dash stuff (no HeroController available)
                _logger?.LogInfo($"RemoteStartSlash executed successfully for {instance.gameObject.name}");
            }
            catch (Exception e)
            {
                _logger?.LogError($"Error in RemoteStartSlash: {e}");
            }
        }

        // Helper method to get full GameObject path for debugging
        private static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        // Patch to redirect remote player slashes to use RemoteStartSlash
        [HarmonyPatch(typeof(NailSlash), "StartSlash")]
        public static class NailSlashStartSlashPatch
        {
            [HarmonyPrefix]
            static bool StartSlashPrefix(NailSlash __instance)
            {
                // Check if this is a remote player's slash by walking up the transform hierarchy
                Transform current = __instance.transform;
                bool isRemotePlayer = false;

                while (current != null)
                {
                    if (current.name.Contains("SilklessCooperator"))
                    {
                        isRemotePlayer = true;
                        break;
                    }
                    current = current.parent;
                }

                if (isRemotePlayer)
                {
                    _logger?.LogInfo($"Redirecting StartSlash to RemoteStartSlash for remote player at path: {GetGameObjectPath(__instance.gameObject)}");

                    // Use our custom RemoteStartSlash method
                    __instance.RemoteStartSlash();

                    // Skip the original method
                    return false;
                }

                // Let the original method run for the local player
                return true;
            }
        }

        // Patch to redirect remote player slashes to use RemotePlaySlash
        [HarmonyPatch(typeof(NailSlash), "PlaySlash")]
        public static class NailSlashPlaySlashPatch
        {
            [HarmonyPrefix]
            static bool PlaySlashPrefix(NailSlash __instance)
            {
                // Check if this is a remote player's slash by walking up the transform hierarchy
                Transform current = __instance.transform;
                bool isRemotePlayer = false;

                while (current != null)
                {
                    if (current.name.Contains("SilklessCooperator"))
                    {
                        isRemotePlayer = true;
                        break;
                    }
                    current = current.parent;
                }

                if (isRemotePlayer)
                {
                    _logger?.LogInfo($"Redirecting PlaySlash to RemotePlaySlash for remote player at path: {GetGameObjectPath(__instance.gameObject)}");

                    // Use our custom RemotePlaySlash method
                    __instance.RemotePlaySlash();

                    // Skip the original method
                    return false;
                }

                // Let the original method run for the local player
                return true;
            }
        }

        // Create a remote-safe version of OnSlashStarting
        public static void RemoteOnSlashStarting(this NailAttackBase instance)
        {
            try
            {
                var traverse = Traverse.Create(instance);

                // Skip nail imbuement (no HeroController available)
                // NailImbuementConfig currentImbuement = this.hc.NailImbuement.CurrentImbuement;
                // this.SetNailImbuement(currentImbuement, this.hc.NailImbuement.CurrentElement);

                // Trigger AttackStarting action if it exists
                var attackStartingAction = traverse.Field("AttackStarting").GetValue() as Action;
                if (attackStartingAction != null)
                {
                    attackStartingAction();
                }

                // Handle ExtraDamager
                var extraDamager = traverse.Field("ExtraDamager").GetValue() as GameObject;
                if (extraDamager != null)
                {
                    extraDamager.SetActive(false);
                }

                // Activate on slash objects
                var activateOnSlash = traverse.Field("activateOnSlash").GetValue();
                if (activateOnSlash != null)
                {
                    var setAllActiveMethod = AccessTools.Method(activateOnSlash.GetType(), "SetAllActive");
                    if (setAllActiveMethod != null)
                    {
                        setAllActiveMethod.Invoke(activateOnSlash, new object[] { true });
                    }
                }

                // Play vibration
                var playVibrationMethod = AccessTools.Method(instance.GetType(), "PlayVibration");
                if (playVibrationMethod != null)
                {
                    playVibrationMethod.Invoke(instance, null);
                }

                // Set scale with LongNeedleTool support using DummyController
                var scale = traverse.Field("scale").GetValue();
                if (scale != null)
                {
                    Vector3 baseScale = (Vector3)scale;

                    // Check for DummyController to get LongNeedle status
                    var dummyController = instance.GetComponentInParent<DummyController>();
                    if (dummyController != null && dummyController.hasLongNeedle)
                    {
                        // Get LongNeedleMultiplier from Gameplay
                        var gameplayType = AccessTools.TypeByName("Gameplay");
                        if (gameplayType != null)
                        {
                            var longNeedleMultiplierProperty = AccessTools.Property(gameplayType, "LongNeedleMultiplier");
                            if (longNeedleMultiplierProperty != null)
                            {
                                Vector2 longNeedleMultiplier = (Vector2)longNeedleMultiplierProperty.GetValue(null);

                                // Apply scaling based on attack direction
                                Vector2 vector = dummyController.isUpAttacking ?
                                    new Vector2(longNeedleMultiplier.y, longNeedleMultiplier.x) :
                                    longNeedleMultiplier;

                                baseScale = new Vector3(baseScale.x * vector.x, baseScale.y * vector.y, baseScale.z);
                                _logger?.LogInfo($"Applied LongNeedle scaling for {dummyController.currentAttackDirection} attack: {vector}");
                            }
                        }
                    }

                    instance.transform.localScale = baseScale;
                }

                _logger?.LogInfo($"RemoteOnSlashStarting executed successfully for {instance.gameObject.name}");
            }
            catch (Exception e)
            {
                _logger?.LogError($"Error in RemoteOnSlashStarting: {e}");
            }
        }

        // Patch NailAttackBase.OnSlashStarting to handle remote players
        [HarmonyPatch(typeof(NailAttackBase), "OnSlashStarting")]
        public static class NailAttackBaseOnSlashStartingPatch
        {
            [HarmonyPrefix]
            static bool OnSlashStartingPrefix(NailAttackBase __instance)
            {
                // Check if this is a remote player's attack
                Transform current = __instance.transform;
                while (current != null)
                {
                    if (current.name.Contains("SilklessCooperator"))
                    {
                        _logger?.LogInfo($"Redirecting OnSlashStarting to RemoteOnSlashStarting for remote player");
                        __instance.RemoteOnSlashStarting();
                        return false; // Skip the original method
                    }
                    current = current.parent;
                }
                return true; // Let the original method run for local player
            }
        }

        // Create a remote-safe version of OnEndedDamage
        public static void RemoteOnEndedDamage(this NailAttackBase instance, bool didHit)
        {
            try
            {
                var traverse = Traverse.Create(instance);

                // Set IsDamagerActive to false
                var isDamagerActiveProperty = traverse.Property("IsDamagerActive");
                if (isDamagerActiveProperty != null)
                {
                    isDamagerActiveProperty.SetValue(false);
                }

                // Trigger EndedDamage action if it exists
                var endedDamageAction = traverse.Field("EndedDamage").GetValue() as System.Action<bool>;
                if (endedDamageAction != null)
                {
                    endedDamageAction(didHit);
                }

                // Skip HeroController SilkTauntEffectConsume call
                // this.hc.SilkTauntEffectConsume(); - Not available for remote players

                _logger?.LogInfo($"RemoteOnEndedDamage executed successfully for {instance.gameObject.name} with didHit: {didHit}");
            }
            catch (Exception e)
            {
                _logger?.LogError($"Error in RemoteOnEndedDamage: {e}");
            }
        }

        // Patch NailAttackBase.OnEndedDamage to handle remote players
        [HarmonyPatch(typeof(NailAttackBase), "OnEndedDamage")]
        public static class NailAttackBaseOnEndedDamagePatch
        {
            [HarmonyPrefix]
            static bool OnEndedDamagePrefix(NailAttackBase __instance, bool didHit)
            {
                // Check if this is a remote player's attack
                Transform current = __instance.transform;
                while (current != null)
                {
                    if (current.name.Contains("SilklessCooperator"))
                    {
                        _logger?.LogInfo($"Redirecting OnEndedDamage to RemoteOnEndedDamage for remote player");
                        __instance.RemoteOnEndedDamage(didHit);
                        return false; // Skip the original method
                    }
                    current = current.parent;
                }
                return true; // Let the original method run for local player
            }
        }
    }
}