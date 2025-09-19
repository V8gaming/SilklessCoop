using HarmonyLib;
using UnityEngine;
using BepInEx.Logging;
using System.Reflection;

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

                try
                {
                    // Add currentAttack field and set it to attackDir
                    var traverse = Traverse.Create(__instance);
                    traverse.Field("currentAttack").SetValue(__0);

                    // Extract attack direction
                    string attackDirection = __0?.ToString() ?? "Unknown";
                    string paramType = __0?.GetType()?.Name ?? "null";

                    _logger?.LogInfo($"Attack parameter type: {paramType}, value: {attackDirection}");

                    // Extract additional HeroController properties
                    var heroTraverse = Traverse.Create(__instance);
                    string playerDataStr = "null";
                    string slashComponentStr = "null";
                    string currentSlashDamagerStr = "null";

                    try
                    {
                        // Extract playerData
                        var playerData = heroTraverse.Property("playerData").GetValue();
                        if (playerData != null)
                        {
                            // Get CurrentCrestID and other relevant properties
                            var playerDataTraverse = Traverse.Create(playerData);
                            var currentCrestID = playerDataTraverse.Property("CurrentCrestID").GetValue();
                            playerDataStr = currentCrestID?.ToString() ?? "0";
                        }

                        // Extract SlashComponent
                        var slashComponent = heroTraverse.Property("SlashComponent").GetValue();
                        if (slashComponent != null)
                        {
                            slashComponentStr = slashComponent.GetType().Name;
                        }

                        // Extract currentSlashDamager
                        var currentSlashDamager = heroTraverse.Property("currentSlashDamager").GetValue();
                        if (currentSlashDamager != null)
                        {
                            currentSlashDamagerStr = currentSlashDamager.GetType().Name;
                        }

                        _logger?.LogInfo($"Extracted HeroController data - PlayerData: {playerDataStr}, SlashComponent: {slashComponentStr}, CurrentSlashDamager: {currentSlashDamagerStr}");
                    }
                    catch (System.Exception extractEx)
                    {
                        _logger?.LogError($"Error extracting HeroController properties: {extractEx}");
                    }

                    // Create composite attack data
                    string attackData = $"{attackDirection}|{playerDataStr}|{slashComponentStr}|{currentSlashDamagerStr}";

                    // Only queue attack if we're connected to avoid connection spam
                    if (_gameSync.IsConnected())
                    {
                        _gameSync.SetCurrentAttack(attackData);
                        _logger?.LogInfo($"Set composite attack data and queued for network: {attackData}");
                    }
                    else
                    {
                        _logger?.LogInfo($"Attack not queued - not connected to server: {attackData}");
                    }
                }
                catch (System.Exception e)
                {
                    _logger?.LogError($"Error in Attack prefix: {e}");
                }
            }

        }
    }
}