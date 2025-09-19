using BepInEx.Logging;
using SilklessCoop.Connectors;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using HarmonyLib;

namespace SilklessCoop
{
    public class GameSync : MonoBehaviour
    {
        public ManualLogSource Logger;
        public ModConfig Config;

        private NetworkInterface _network;
        private Connector _connector;

        private float _tickTimeout;
        private string _id => _connector.GetId();

        // sprite sync - self
        private GameObject _hornetObject = null;
        private tk2dSprite _hornetSprite = null;
        private Rigidbody2D _hornetRigidbody = null;
        private BoxCollider2D _hornetBoxCollider = null;
        private MonoBehaviour _heroController = null;
        private Dictionary<string, tk2dSpriteCollectionData> _collectionCache = new Dictionary<string, tk2dSpriteCollectionData>();

        // sprite sync - others
        private Dictionary<string, GameObject> _playerObjects = new Dictionary<string, GameObject>();
        private Dictionary<string, tk2dSprite> _playerSprites = new Dictionary<string, tk2dSprite>();
        private Dictionary<string, SimpleInterpolator> _playerInterpolators = new Dictionary<string, SimpleInterpolator>();

        // Your custom features
        private Dictionary<string, BoxCollider2D> _playerColliders = new Dictionary<string, BoxCollider2D>();
        private Dictionary<string, string> _playerColors = new Dictionary<string, string>();
        private Dictionary<string, bool> _hasAppliedCopies = new Dictionary<string, bool>();

        // map sync - self
        private GameObject _map = null;
        private GameObject _mainQuests = null;
        private GameObject _compass = null;

        // map sync - others
        private Dictionary<string, GameObject> _playerCompasses = new Dictionary<string, GameObject>();
        private Dictionary<string, tk2dSprite> _playerCompassSprites = new Dictionary<string, tk2dSprite>();

        // player count
        private HashSet<string> _playerIds = new HashSet<string>();
        private List<GameObject> _playerCountPins = new List<GameObject>();

        // Attack system
        private bool _setup = false;
        private string _currentAttack = null;
        private string _lastAttackData = null;

        // Helper methods for color parsing
        private static float parseFloat(string s)
        {
            s = s.Replace(",", ".");
            if (s.IndexOf('.') < 0) return float.Parse(s);

            string before = s.Split(".")[0];
            string after = s.Split(".")[1];

            float fbefore = parseFloat(before);
            float fafter = parseFloat(after) / MathF.Pow(10, after.Length);

            return fbefore + fafter;
        }

        private static Color parseColor(string colorString)
        {
            string[] parts = colorString.Split(',');
            if (parts.Length >= 3)
            {
                float r = parseFloat(parts[0]);
                float g = parseFloat(parts[1]);
                float b = parseFloat(parts[2]);
                return new Color(r, g, b, 0.7f);
            }
            return new Color(1, 1, 1, 0.7f);
        }

        private void Start()
        {
            _network = GetComponent<NetworkInterface>();
            _connector = GetComponent<Connector>();

            _network.AddHandler<PacketTypes.JoinPacket>(OnJoinPacket);
            _network.AddHandler<PacketTypes.LeavePacket>(OnLeavePacket);
            _network.AddHandler<PacketTypes.HornetPositionPacket>(OnHornetPositionPacket);
            _network.AddHandler<PacketTypes.HornetAnimationPacket>(OnHornetAnimationPacket);
            _network.AddHandler<PacketTypes.CompassPositionPacket>(OnCompassPositionPacket);
            _network.AddHandler<PacketTypes.AttackPacket>(OnAttackPacket);
        }

        private void Update()
        {
            // tick
            if (_tickTimeout >= 0)
            {
                _tickTimeout -= Time.unscaledDeltaTime;
                if (_tickTimeout <= 0)
                    { Tick(); _tickTimeout = 1.0f / Config.TickRate; }
            }

            // setup references
            if (!_hornetObject) _hornetObject = GameObject.Find("Hero_Hornet");
            if (!_hornetObject) _hornetObject = GameObject.Find("Hero_Hornet(Clone)");
            if (!_hornetObject) _hornetObject = GameObject.FindGameObjectWithTag("Player");
            if (_hornetObject && !_hornetObject.name.Contains("Hero_Hornet")) _hornetObject = null;
            if (!_hornetObject) { _setup = false; return; }

            if (_hornetObject && !_hornetRigidbody) _hornetRigidbody = _hornetObject.GetComponent<Rigidbody2D>();
            if (_hornetObject && !_hornetSprite) _hornetSprite = _hornetObject.GetComponent<tk2dSprite>();
            if (_hornetObject && !_hornetBoxCollider) _hornetBoxCollider = _hornetObject.GetComponent<BoxCollider2D>();
            if (!_heroController) _heroController = _hornetObject?.GetComponent("HeroController") as MonoBehaviour;

            if (_hornetSprite && _collectionCache.Count == 0)
                foreach (tk2dSpriteCollectionData c in Resources.FindObjectsOfTypeAll<tk2dSpriteCollectionData>())
                    _collectionCache[c.spriteCollectionGUID] = c;

            if (!_map) _map = GameObject.Find("Game_Map_Hornet");
            if (!_map) _map = GameObject.Find("Game_Map_Hornet(Clone)");
            if (_map && !_mainQuests) _mainQuests = _map.transform.Find("Main Quest Pins")?.gameObject;
            if (_map && !_compass) _compass = _map.transform.Find("Compass Icon")?.gameObject;

            if (!_setup && _hornetObject && _hornetSprite && _hornetRigidbody && _hornetBoxCollider)
            {
                Logger.LogInfo("GameObject setup complete.");
                _setup = true;
            }

            UpdateUI();
        }

        private void UpdateUI()
        {
            foreach (GameObject g in _playerCompasses.Values)
                if (g) g.SetActive(true);

            if (_compass)
            {
                while (_playerCountPins.Count < _playerIds.Count)
                {
                    GameObject newObject = Instantiate(_compass, _map.transform);
                    newObject.SetActive(true);
                    newObject.SetName("SilklessPlayerCount");
                    newObject.transform.position = new Vector3(-14.8f + 0.6f * _playerCountPins.Count, -8.2f, 0);
                    newObject.transform.localScale = new Vector3(0.6f, 0.6f, 1);

                    _playerCountPins.Add(newObject);
                }

                while (_playerCountPins.Count > _playerIds.Count)
                {
                    Destroy(_playerCountPins.Last());
                    _playerCountPins.RemoveAt(_playerCountPins.Count - 1);
                }

                for (int i = 0; i < _playerCountPins.Count; i++)
                {
                    _playerCountPins[i].transform.position = new Vector3(-14.8f + 0.6f * i, -8.2f, 0);
                    _playerCountPins[i].SetActive(_mainQuests.activeSelf);
                }
            }
        }

        private void Tick()
        {
            if (!_connector.Initialized || !_connector.Enabled || !_connector.Connected) return;

            SendHornetPositionPacket();
            SendHornetAnimationPacket();
            SendCompassPositionPacket();
            SendAttackPacket();
        }

        // Attack system methods
        public void SetCurrentAttack(string attackData)
        {
            if (_connector.Connected)
            {
                _currentAttack = attackData;
            }
        }

        public bool IsConnected()
        {
            return _connector != null && _connector.Connected;
        }

        public string GetCurrentAttack()
        {
            string attack = _currentAttack;
            _currentAttack = null; // Clear after reading to avoid sending duplicates
            return attack;
        }

        public void Reset()
        {
            foreach (GameObject g in _playerObjects.Values)
                if (g) Destroy(g);

            foreach (GameObject g in _playerCompasses.Values)
                if (g) Destroy(g);

            foreach (GameObject g in _playerCountPins)
                if (g) Destroy(g);
            _playerCountPins.Clear();

            _playerObjects.Clear();
            _playerSprites.Clear();
            _playerInterpolators.Clear();
            _playerColliders.Clear();
            _playerColors.Clear();
            _hasAppliedCopies.Clear();
        }

        private void OnJoinPacket(PacketTypes.JoinPacket packet)
        {
            _playerIds.Add(packet.id);

            if (Config.PrintDebugOutput) Logger.LogInfo($"Player {packet.id} joined ({packet.version}).");
        }
        private void OnLeavePacket(PacketTypes.LeavePacket packet)
        {
            _playerIds.Remove(packet.id);

            if (_playerObjects.TryGetValue(packet.id, out GameObject g1) && g1 != null) Destroy(g1);
            if (_playerCompasses.TryGetValue(packet.id, out GameObject g2) && g2 != null) Destroy(g2);

            // Clean up your custom feature dictionaries
            _playerColliders.Remove(packet.id);
            _playerColors.Remove(packet.id);
            _hasAppliedCopies.Remove(packet.id);

            if (Config.PrintDebugOutput) Logger.LogInfo($"Player {packet.id} left.");
        }

        private void SendHornetPositionPacket()
        {
            if (!_hornetObject || !_hornetRigidbody || !_hornetBoxCollider) return;

            _network.SendPacket(new PacketTypes.HornetPositionPacket
            {
                id = _id,
                scene = SceneManager.GetActiveScene().name,
                posX = _hornetObject.transform.position.x,
                posY = _hornetObject.transform.position.y,
                scaleX = _hornetObject.transform.localScale.x,
                vX = _hornetRigidbody.linearVelocity.x * Time.timeScale,
                vY = _hornetRigidbody.linearVelocity.y * Time.timeScale,
                // Your custom features
                sizeX = _hornetBoxCollider.size.x,
                sizeY = _hornetBoxCollider.size.y,
                offsetX = _hornetBoxCollider.offset.x,
                offsetY = _hornetBoxCollider.offset.y,
                playerColor = Config.PlayerColor
            });
        }
        private void OnHornetPositionPacket(PacketTypes.HornetPositionPacket packet)
        {
            _playerIds.Add(packet.id);

            if (!_hornetObject) return;

            if (packet.scene != SceneManager.GetActiveScene().name)
            {
                if (_playerObjects.TryGetValue(packet.id, out GameObject playerObject) && playerObject)
                {
                    Destroy(playerObject);
                }
                if (_playerCompasses.TryGetValue(packet.id, out GameObject compassObject) && compassObject)
                {
                    Destroy(compassObject);
                }
            } else
            {
                if (!_playerObjects.TryGetValue(packet.id, out GameObject playerObject) || !playerObject)
                {
                    // create player
                    if (Config.PrintDebugOutput) Logger.LogInfo($"Creating new player object for player {packet.id}...");

                    GameObject newObject = new GameObject();
                    newObject.SetName($"SilklessCooperator - {packet.id}");
                    newObject.transform.SetParent(transform);
                    newObject.transform.position = new Vector3(packet.posX, packet.posY, _hornetObject.transform.position.z + 0.001f);
                    newObject.transform.localScale = new Vector3(packet.scaleX, 1, 1);

                    tk2dSprite newSprite = tk2dSprite.AddComponent(newObject, _hornetSprite.Collection, _hornetSprite.spriteId);
                    newSprite.color = parseColor(packet.playerColor);

                    SimpleInterpolator newInterpolator = newObject.AddComponent<SimpleInterpolator>();
                    newInterpolator.velocity = new Vector3(packet.vX, packet.vY, 0);

                    // Your custom features
                    BoxCollider2D newCollider = null;
                    if (Config.EnableCollision)
                    {
                        newCollider = newObject.AddComponent<BoxCollider2D>();
                        newCollider.size = new Vector2(packet.sizeX, packet.sizeY);
                        newCollider.offset = new Vector2(packet.offsetX, packet.offsetY);
                        Logger.LogInfo($"Added BoxCollider2D for player {packet.id}");
                    }

                    // Copy attacks for PvP
                    if (Config.EnablePvP)
                    {
                        CopySpecificChild(_hornetObject, newObject, "Attacks", packet.id);
                        //ReplaceNailSlashComponents(newObject);
                        Logger.LogInfo($"Copied Attacks child and replaced NailSlash components for player {packet.id}");
                    }

                    _playerObjects[packet.id] = newObject;
                    _playerSprites[packet.id] = newSprite;
                    _playerInterpolators[packet.id] = newInterpolator;
                    _playerColliders[packet.id] = newCollider;
                    _playerColors[packet.id] = packet.playerColor;
                    _hasAppliedCopies[packet.id] = Config.EnablePvP;

                    if (Config.PrintDebugOutput) Logger.LogInfo($"Created new player object for player {packet.id}.");
                }
                else
                {
                    if (!_playerInterpolators.TryGetValue(packet.id, out SimpleInterpolator playerInterpolator)) return;

                    // update player
                    playerObject.transform.position = new Vector3(packet.posX, packet.posY, _hornetObject.transform.position.z + 0.001f);
                    playerObject.transform.localScale = new Vector3(packet.scaleX, 1, 1);
                    playerInterpolator.velocity = new Vector3(packet.vX, packet.vY, 0);

                    // Update color
                    if (_playerSprites.TryGetValue(packet.id, out tk2dSprite playerSprite))
                    {
                        playerSprite.color = parseColor(packet.playerColor);
                    }

                    // Update collider
                    if (_playerColliders.TryGetValue(packet.id, out BoxCollider2D playerCollider) && playerCollider != null)
                    {
                        playerCollider.size = new Vector2(packet.sizeX, packet.sizeY);
                        playerCollider.offset = new Vector2(packet.offsetX, packet.offsetY);
                    }

                    if (Config.PrintDebugOutput) Logger.LogInfo($"Updated position of player {packet.id} to ({packet.posX} {packet.posY})");
                }
            }
        }

        private void SendHornetAnimationPacket()
        {
            if (!_hornetSprite) return;

            _network.SendPacket(new PacketTypes.HornetAnimationPacket
            {
                id = _id,
                collectionGuid = _hornetSprite.Collection.spriteCollectionGUID,
                spriteId = _hornetSprite.spriteId,
            });
        }
        private void OnHornetAnimationPacket(PacketTypes.HornetAnimationPacket packet)
        {
            _playerIds.Add(packet.id);

            if (!_hornetObject) return;
            if (!_playerSprites.TryGetValue(packet.id, out tk2dSprite playerSprite) || !playerSprite) return;
            if (!_collectionCache.TryGetValue(packet.collectionGuid, out tk2dSpriteCollectionData collectionData) || !collectionData) return;

            playerSprite.Collection = collectionData;
            playerSprite.spriteId = packet.spriteId;

            if (Config.PrintDebugOutput) Logger.LogInfo($"Set sprite for player {packet.id} to {packet.collectionGuid}/{packet.spriteId}");
        }

        private void SendCompassPositionPacket()
        {
            if (!_hornetObject || !_compass) return;

            _network.SendPacket(new PacketTypes.CompassPositionPacket
            {
                id = _id,
                active = _mainQuests.activeSelf,
                posX = _compass.transform.localPosition.x,
                posY = _compass.transform.localPosition.y,
            });
        }
        private void OnCompassPositionPacket(PacketTypes.CompassPositionPacket packet)
        {
            _playerIds.Add(packet.id);

            if (!_map || !_compass || !_mainQuests) return;
            if (!_playerCompasses.TryGetValue(packet.id, out GameObject playerCompass) || !playerCompass) {
                // create compass
                if (Config.PrintDebugOutput) Logger.LogInfo($"Creating new compass object for player {packet.id}...");

                GameObject newObject = Instantiate(_compass, _map.transform);
                newObject.SetActive(_mainQuests.activeSelf);
                newObject.SetName($"SilklessCompass - {packet.id}");
                newObject.transform.localPosition = new Vector2(packet.posX, packet.posY);

                tk2dSprite newSprite = newObject.GetComponent<tk2dSprite>();
                newSprite.color = new Color(1, 1, 1, Config.ActiveCompassOpacity);

                _playerCompasses[packet.id] = newObject;
                _playerCompassSprites[packet.id] = newSprite;

                if (Config.PrintDebugOutput) Logger.LogInfo($"Created new compass object for player {packet.id}.");
            } else
            {
                if (!_playerCompassSprites.TryGetValue(packet.id, out tk2dSprite compassSprite) || !compassSprite) return;

                // update compass
                playerCompass.transform.localPosition = new Vector2(packet.posX, packet.posY);
                compassSprite.color = new Color(1, 1, 1, packet.active ? Config.ActiveCompassOpacity : Config.InactiveCompassOpacity);

                if (Config.PrintDebugOutput) Logger.LogInfo($"Updated position of compass {packet.id} to ({packet.posX} {packet.posY})");
            }
        }

        // Attack system
        private void SendAttackPacket()
        {
            string attack = GetCurrentAttack();
            if (attack != null)
            {
                _network.SendPacket(new PacketTypes.AttackPacket
                {
                    id = _id,
                    attackData = attack
                });

                if (Config.PrintDebugOutput) Logger.LogInfo($"Sent attack packet: {attack}");
            }
        }

        private void OnAttackPacket(PacketTypes.AttackPacket packet)
        {
            if (Config.PrintDebugOutput) Logger.LogInfo($"Received attack packet from {packet.id}: {packet.attackData}");

            if (Config.EnablePvP)
            {
                ApplyAttack(packet.attackData, packet.id);
            }
        }

        public void ApplyAttack(string attackData, string playerId)
        {
            if (!_setup || string.IsNullOrEmpty(attackData) || !_playerObjects.ContainsKey(playerId)) return;

            try
            {
                var playerObject = _playerObjects[playerId];
                if (playerObject == null) return;

                Logger.LogInfo($"Remote player {playerId} performed attack: {attackData}");

                // Parse the enhanced attack data format: direction|playerData|slashComponent|damager
                string attackDirection = "Unknown";
                string playerDataInfo = "null";
                string slashComponentInfo = "null";
                string damagerInfo = "null";

                try
                {
                    string[] attackParts = attackData.Split('|');
                    if (attackParts.Length >= 4)
                    {
                        attackDirection = attackParts[0];
                        playerDataInfo = attackParts[1];
                        slashComponentInfo = attackParts[2];
                        damagerInfo = attackParts[3];

                        Logger.LogInfo($"Parsed attack data - Direction: {attackDirection}, PlayerData: {playerDataInfo}, SlashComponent: {slashComponentInfo}, Damager: {damagerInfo}");
                    }
                    else
                    {
                        // Fallback for old format (just direction)
                        attackDirection = attackData;
                        Logger.LogInfo($"Using fallback format - Direction only: {attackDirection}");
                    }
                }
                catch (Exception parseEx)
                {
                    Logger.LogError($"Error parsing enhanced attack data: {parseEx}");
                }

                // Trigger slash effects on remote player using our custom Slash components
                var slashComponents = playerObject.GetComponentsInChildren<Slash>();
                foreach (var slash in slashComponents)
                {
                    if (slash != null)
                    {
                        // Configure slash with parsed HeroController data before starting
                        slash.ConfigureFromHeroControllerData(attackDirection, playerDataInfo, slashComponentInfo, damagerInfo);
                        slash.StartSlash();
                        Logger.LogInfo($"Triggered configured slash on {slash.gameObject.name}");
                    }
                }

                if (slashComponents.Length == 0)
                {
                    Logger.LogWarning($"No Slash components found for remote player {playerId}");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Error applying remote attack: {e}");
            }
        }

        private void ReplaceNailSlashComponents(GameObject playerObject)
        {
            if (playerObject == null) return;

            try
            {
                // Find all NailSlash components in the player object and its children
                var nailSlashComponents = playerObject.GetComponentsInChildren<Component>()
                    .Where(c => c != null && c.GetType().Name.Contains("NailSlash"))
                    .ToArray();

                Logger.LogInfo($"Found {nailSlashComponents.Length} NailSlash components to replace");

                foreach (var originalSlash in nailSlashComponents)
                {
                    if (originalSlash == null) continue;

                    try
                    {
                        // Add our custom Slash component to the same GameObject
                        var newSlash = originalSlash.gameObject.AddComponent<Slash>();

                        // Remove the original NailSlash component
                        DestroyImmediate(originalSlash);

                        Logger.LogInfo($"Replaced NailSlash component on {originalSlash.gameObject.name}");
                    }
                    catch (Exception slashEx)
                    {
                        Logger.LogError($"Error replacing NailSlash component: {slashEx}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in ReplaceNailSlashComponents: {ex}");
            }
        } 

        public void CopySpecificChild(GameObject sourceParent, GameObject targetParent, string childFolderName, string id)
        {
            if (sourceParent == null || targetParent == null) return;

            // Return if already applied
            if (_hasAppliedCopies.ContainsKey(id) && _hasAppliedCopies[id])
            {
                Debug.Log($"'{childFolderName}' already copied from {sourceParent.name} to {targetParent.name}");
                return;
            }

            // Find the specific child folder in source
            Transform sourceChild = sourceParent.transform.Find(childFolderName);
            if (sourceChild == null)
            {
                Debug.LogWarning($"Child folder '{childFolderName}' not found in {sourceParent.name}");
                return;
            }

            // Check if it already exists in target
            Transform existingChild = targetParent.transform.Find(childFolderName);
            if (existingChild != null)
            {
                Debug.Log($"'{childFolderName}' already exists in {targetParent.name}, skipping");
                return;
            }

            // Copy the folder and all its contents
            GameObject copiedChild = UnityEngine.Object.Instantiate(sourceChild.gameObject, targetParent.transform);
            copiedChild.name = childFolderName; // Remove "(Clone)" suffix

            // Mark as applied
            _hasAppliedCopies[id] = true;
            Debug.Log($"Successfully copied '{childFolderName}' from {sourceParent.name} to {targetParent.name}");
        }
    }
}