﻿using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static ArchipelagoRandomizer.MessageBoxHandler;

namespace ArchipelagoRandomizer
{
	public class ItemRandomizer : MonoBehaviour
	{
		private static ItemRandomizer instance;
		private static List<LocationData.Location> locations;
		private static FadeEffectData fadeData;

		private MessageBoxHandler itemMessageHandler;
		private ItemHandler itemHandler;
		private DeathLinkHandler deathLinkHandler;
		private HintHandler hintHandler;
		private GoalHandler goalHandler;
		private Entity player;
		private SaverOwner mainSaver;
		private bool rollOpensChests;

		public static ItemRandomizer Instance { get { return instance; } }
		public static bool IsActive { get; private set; }
		public FadeEffectData FadeData { get { return fadeData; } }

		public void OnFileStart(bool newFile, APFileData apFileData)
		{
			mainSaver = ModCore.Plugin.MainSaver;
			itemHandler = gameObject.AddComponent<ItemHandler>();
			itemMessageHandler = MessageBoxHandler.Instance;
			deathLinkHandler = apFileData.Deathlink ? gameObject.AddComponent<DeathLinkHandler>() : null;
			hintHandler = gameObject.AddComponent<HintHandler>();
			goalHandler = gameObject.AddComponent<GoalHandler>();

			Events.OnPlayerSpawn += OnPlayerSpawn;
			Events.OnSceneLoaded += OnSceneLoaded;

			rollOpensChests = Convert.ToBoolean(APHandler.GetSlotData<long>("roll_opens_chests"));

			if (newFile)
				SetupNewFile(apFileData);

			APHandler.Instance.SyncItemsWithServer();
			IsActive = true;
			Plugin.Log.LogInfo("ItemRandomizer is enabled!");
		}

		public void RollToOpenChest(List<CollisionDetector.CollisionData> collisions)
		{
			if (!rollOpensChests)
				return;

			foreach (CollisionDetector.CollisionData collision in collisions)
			{
				bool isChest = collision.gameObject.GetComponentInParent<SpawnItemEventObserver>() != null;

				if (!isChest)
					continue;

				Transform crystal = collision.transform.parent.Find("crystal");
				bool isLockedByCrystal = crystal != null && crystal.gameObject.activeSelf;

				if (isLockedByCrystal)
					continue;

				DummyAction action = crystal == null ?
					collision.transform.GetComponentInParent<DummyAction>() :
					collision.transform.parent.GetChild(0).GetComponent<DummyAction>();

				if (action == null || action.hasFired)
					continue;

				action.Fire(false);
			}
		}

		public void LocationChecked(string saveFlag, string sceneName)
		{
			if (string.IsNullOrEmpty(saveFlag))
				return;

			LocationData.Location location = locations.Find(x => (string.IsNullOrEmpty(x.SceneName) || x.SceneName == sceneName) && x.Flag == saveFlag);

			if (location == null)
			{
				Plugin.Log.LogError($"No location with save flag {saveFlag} in {sceneName} was found in JSON data, so location will not be marked on Archipelago server!");
				return;
			}

			APHandler.Instance.LocationChecked(location.Offset);
		}

		public IEnumerator ItemSent(string itemName, string playerName)
		{
			// Wait for player to spawn
			while (player == null)
				yield return null;

			MessageData messageData = new()
			{
				Item = itemHandler.GetItemData(itemName),
				ItemName = itemName,
				PlayerName = playerName,
				MessageType = MessageType.Sent
			};
			itemMessageHandler.ShowMessageBox(messageData);
		}

		public IEnumerator ItemReceived(int offset, string itemName, string sentFromPlayer)
		{
			ItemHandler.ItemData.Item item = itemHandler.GetItemData(offset);

			// Do nothing if null item
			if (item == null)
				yield return null;

			// Wait for ItemHandler to initialize
			while (!itemHandler.HasInitialized)
				yield return null;

			// Wait for player to spawn
			while (player == null)
				yield return null;

			// Assign item
			itemHandler.GiveItem(item);

			yield return new WaitForEndOfFrame();

			// Send item get message
			MessageData messageData = new()
			{
				Item = item,
				ItemName = itemName,
				PlayerName = sentFromPlayer,
				MessageType = sentFromPlayer == APHandler.Instance.CurrentPlayer.Name ?
					MessageType.ReceivedFromSelf :
					MessageType.ReceivedFromSomeone
			};
			itemMessageHandler.ShowMessageBox(messageData);
		}

		public IEnumerator OnDisconnected()
		{
			yield return new WaitForEndOfFrame();
			mainSaver.SaveAll();
			SceneDoor.StartLoad("MainMenu", "", fadeData);
		}

		private void Awake()
		{
			instance = this;

			// Parse data JSON
			if (locations == null)
			{
				string path = BepInEx.Utility.CombinePaths(BepInEx.Paths.PluginPath, PluginInfo.PLUGIN_NAME, "Data", "locationData.json");

				if (!ModCore.Utility.TryParseJson(path, out LocationData? data))
				{
					Plugin.Log.LogError("ItemRandomizer JSON data has failed to load! The randomizer will not start!");
					Destroy(this);
					return;
				}

				locations = data?.Locations;
			}

			// Setup fade data
			if (fadeData == null)
			{
				fadeData = new()
				{
					_targetColor = Color.black,
					_fadeOutTime = 0.5f,
					_fadeInTime = 1.25f,
					_faderName = "ScreenCircleWipe",
					_useScreenPos = true
				};
			}

			DontDestroyOnLoad(this);
		}

		private void OnDisable()
		{
			IsActive = false;

			Events.OnPlayerSpawn -= OnPlayerSpawn;
			Events.OnSceneLoaded -= OnSceneLoaded;

			// If disconnected, show message
			if (!APHandler.Instance.IsConnected)
			{
				MessageData messageData = new()
				{
					Message = "Oh no! You were disconnected from the server!"
				};
				itemMessageHandler.ShowMessageBox(messageData);
			}

			APHandler.Instance.Disconnect();

			Plugin.Log.LogInfo("ItemRandomizer is disabled!");
		}

		private void SetupNewFile(APFileData apFileData)
		{
			APMenuStuff.Instance.SaveAPDataToFile(apFileData);

			IDataSaver settingsSaver = mainSaver.LocalStorage.GetLocalSaver("settings");
			settingsSaver.SaveInt("hideMapHint", 1);
			settingsSaver.SaveInt("hideCutscenes", 1);
			settingsSaver.SaveInt("easyMode", 1);
			IDataSaver playerVarsSaver = mainSaver.GetSaver("/local/player/vars");
			playerVarsSaver.SaveInt("melee", -1);

			// Read slot data
			bool keysey = APHandler.GetSlotData<long>("key_settings") == 2;
			bool openD8 = Convert.ToBoolean(APHandler.GetSlotData<long>("open_d8"));
			bool openS4 = Convert.ToBoolean(APHandler.GetSlotData<long>("open_s4"));
			//bool openDW = Convert.ToBoolean(APHandler.GetSlotData<long>("open_dreamworld"));
			bool startWithTracker = Convert.ToBoolean(APHandler.GetSlotData<long>("start_with_tracker"));
			bool startWithWarps = Convert.ToBoolean(APHandler.GetSlotData<long>("start_with_all_warps"));

			if (openD8)
				mainSaver.GetSaver("/local/levels/LonelyRoad/A").SaveInt("PasselDoorYes", 1);
			if (openS4)
			{
				IDataSaver lr2Saver = mainSaver.GetSaver("/local/levels/LonelyRoad2/A");
				lr2Saver.SaveInt("LonelyRoad_secretwall_lock-3--18", 1);
				lr2Saver.SaveInt("LonelyRoad_secretwall_lock-5--18", 1);
				lr2Saver.SaveInt("LonelyRoad_secretwall_lock-11--18", 1);
				lr2Saver.SaveInt("LonelyRoad_secretwall_lock-13--18", 1);
				lr2Saver.SaveInt("LonelyRoad_secretwall_gate-8--17", 1);
			}

			if (keysey)
				UnlockDoors();
			if (startWithTracker)
				ObtainedTracker3();
			if (startWithWarps)
				UnlockWarpGarden();

			mainSaver.SaveLocal();
		}

		private void UnlockDoors()
		{
			// Pillow Fort
			mainSaver.GetSaver("/local/levels/PillowFort/G").SaveInt("PuzzleDoor_locked-6--25", 1);
			mainSaver.GetSaver("/local/levels/PillowFort/K").SaveInt("PuzzleDoor_locked-31--40", 1);
			mainSaver.GetSaver("/local/levels/PillowFort/D").SaveInt("PuzzleDoor_locked-7--21", 1);
			mainSaver.GetSaver("/local/levels/PillowFort/H").SaveInt("PuzzleDoor_locked-28--39", 1);
			// San dCastle
			mainSaver.GetSaver("/local/levels/SandCastle/I").SaveInt("PuzzleDoor_locked-6--25", 1);
			mainSaver.GetSaver("/local/levels/SandCastle/H").SaveInt("PuzzleDoor_locked-69--34", 1);
			mainSaver.GetSaver("/local/levels/SandCastle/E").SaveInt("PuzzleDoor_locked-7--22", 1);
			mainSaver.GetSaver("/local/levels/SandCastle/K").SaveInt("PuzzleDoor_locked-68--37", 1);
			// Art Exhibit
			mainSaver.GetSaver("/local/levels/ArtExhibit/R").SaveInt("PuzzleDoor_locked-38--73", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/C").SaveInt("PuzzleDoor_locked-31--6", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/L").SaveInt("PuzzleDoor_locked-13--53", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/B").SaveInt("PuzzleDoor_locked-28--17", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/E").SaveInt("PuzzleDoor_locked-31--18", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/J").SaveInt("PuzzleDoor_locked-17--54", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/P").SaveInt("PuzzleDoor_locked-39--70", 1);
			mainSaver.GetSaver("/local/levels/ArtExhibit/B").SaveInt("PuzzleDoor_locked-28--5", 1);
			// Trash Cave
			mainSaver.GetSaver("/local/levels/TrashCave/E").SaveInt("PuzzleDoor_locked-18--13", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/H").SaveInt("PuzzleDoor_locked-34--28", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/I").SaveInt("PuzzleDoor_locked-51--34", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/D").SaveInt("PuzzleDoor_locked-79--10", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/L").SaveInt("PuzzleDoor_locked-50--39", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/F").SaveInt("PuzzleDoor_locked-78--13", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/E").SaveInt("PuzzleDoor_locked-28--27", 1);
			mainSaver.GetSaver("/local/levels/TrashCave/A").SaveInt("PuzzleDoor_locked-19--10", 1);
			// Flooded Basement
			mainSaver.GetSaver("/local/levels/FloodedBasement/P").SaveInt("PuzzleDoor_locked-53--46", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/F").SaveInt("PuzzleDoor_locked-33--22", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/K").SaveInt("PuzzleDoor_locked-32--25", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/Q").SaveInt("PuzzleDoor_locked-70--46", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/D").SaveInt("PuzzleDoor_locked-86--10", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/T").SaveInt("PuzzleDoor_locked-69--49", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/H").SaveInt("PuzzleDoor_locked-85--13", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/T").SaveInt("PuzzleDoor_locked-52--49", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/H").SaveInt("PuzzleDoor_locked-61--17", 1);
			mainSaver.GetSaver("/local/levels/FloodedBasement/G").SaveInt("PuzzleDoor_locked-58--16", 1);
			// Potassium Mine
			mainSaver.GetSaver("/local/levels/PotassiumMine/O").SaveInt("PuzzleDoor_locked-84--37", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/M").SaveInt("PuzzleDoor_locked-58--55", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/M").SaveInt("PuzzleDoor_locked-58--64", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/B").SaveInt("PuzzleDoor_locked-56--10", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/R").SaveInt("PuzzleDoor_locked-62--56", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/K").SaveInt("PuzzleDoor_locked-85--34", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/E").SaveInt("PuzzleDoor_locked-55--13", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/S").SaveInt("PuzzleDoor_locked-61--65", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/I").SaveInt("PuzzleDoor_locked-53--25", 1);
			mainSaver.GetSaver("/local/levels/PotassiumMine/E").SaveInt("PuzzleDoor_locked-54--22", 1);
			// Boiling Grave
			mainSaver.GetSaver("/local/levels/BoilingGrave/S").SaveInt("PuzzleDoor_locked-32--66", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/P").SaveInt("PuzzleDoor_locked-58--53", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/M").SaveInt("PuzzleDoor_locked-34--38", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/Q").SaveInt("PuzzleDoor_locked-62--54", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/H").SaveInt("PuzzleDoor_locked-42--27", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/L").SaveInt("PuzzleDoor_locked-35--34", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/G").SaveInt("PuzzleDoor_locked-17--19", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/A").SaveInt("PuzzleDoor_locked-13--18", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/R").SaveInt("PuzzleDoor_locked-26--65", 1);
			mainSaver.GetSaver("/local/levels/BoilingGrave/I").SaveInt("PuzzleDoor_locked-49--28", 1);
			// Grand Library
			mainSaver.GetSaver("/local/levels/GrandLibrary/X").SaveInt("PuzzleDoor_locked-37--49", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/O").SaveInt("PuzzleDoor_locked-79--25", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/R").SaveInt("PuzzleDoor_locked-38--46", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/N").SaveInt("PuzzleDoor_locked-67--25", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/I").SaveInt("PuzzleDoor_locked-76--16", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/D").SaveInt("PuzzleDoor_locked-99--22", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/O").SaveInt("PuzzleDoor_locked-98--25", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/W").SaveInt("PuzzleDoor_locked-28--52", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/E").SaveInt("PuzzleDoor_locked-23--22", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/K").SaveInt("PuzzleDoor_locked-22--25", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/H").SaveInt("PuzzleDoor_locked-68--22", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/H").SaveInt("PuzzleDoor_locked-73--15", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/W").SaveInt("PuzzleDoor_locked-24--49", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/X").SaveInt("PuzzleDoor_locked-32--53", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/I").SaveInt("PuzzleDoor_locked-80--22", 1);
			mainSaver.GetSaver("/local/levels/GrandLibrary/Q").SaveInt("PuzzleDoor_locked-25--46", 1);
			// Sunken Labyrinth
			mainSaver.GetSaver("/local/levels/SunkenLabyrinth/H").SaveInt("PuzzleDoor_locked-13--30", 1);
			mainSaver.GetSaver("/local/levels/SunkenLabyrinth/D").SaveInt("PuzzleDoor_locked-16--31", 1);
			mainSaver.GetSaver("/local/levels/SunkenLabyrinth/P").SaveInt("PuzzleDoor_locked-33--55", 1);
			mainSaver.GetSaver("/local/levels/SunkenLabyrinth/U").SaveInt("PuzzleDoor_locked-31--80", 1);
			mainSaver.GetSaver("/local/levels/SunkenLabyrinth/T").SaveInt("PuzzleDoor_locked-26--79", 1);
			mainSaver.GetSaver("/local/levels/SunkenLabyrinth/K").SaveInt("PuzzleDoor_locked-28--54", 1);
			// Machine Fortress
			mainSaver.GetSaver("/local/levels/MachineFortress/L").SaveInt("PuzzleDoor_locked-38--62", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/F").SaveInt("PuzzleDoor_locked-5--38", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/D").SaveInt("PuzzleDoor_locked-39--21", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/H").SaveInt("PuzzleDoor_locked-5--50", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/E").SaveInt("PuzzleDoor_locked-6--33", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/F").SaveInt("PuzzleDoor_locked-6--45", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/D").SaveInt("PuzzleDoor_locked-21--14", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/H").SaveInt("PuzzleDoor_locked-39--57", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/E").SaveInt("PuzzleDoor_locked-38--26", 1);
			mainSaver.GetSaver("/local/levels/MachineFortress/A").SaveInt("PuzzleDoor_locked-22--10", 1);
			// Dark Hypostyle
			mainSaver.GetSaver("/local/levels/DarkHypostyle/N").SaveInt("PuzzleDoor_locked-13--39", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/G").SaveInt("PuzzleDoor_locked-43--19", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/K").SaveInt("PuzzleDoor_locked-13--29", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/P").SaveInt("PuzzleDoor_locked-58--40", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/F").SaveInt("PuzzleDoor_locked-17--20", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/Q").SaveInt("PuzzleDoor_locked-61--41", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/H").SaveInt("PuzzleDoor_locked-46--20", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/E").SaveInt("PuzzleDoor_locked-13--19", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/F").SaveInt("PuzzleDoor_locked-17--40", 1);
			mainSaver.GetSaver("/local/levels/DarkHypostyle/F").SaveInt("PuzzleDoor_locked-17--30", 1);
			// Tomb of Simulacrum
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AP").SaveInt("PuzzleDoor_locked-88--77", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AQ").SaveInt("PuzzleDoor_locked-91--78", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/P").SaveInt("PuzzleDoor_locked-17--30", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/C").SaveInt("PuzzleDoor_locked-43--5", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AH").SaveInt("PuzzleDoor_locked-52--61", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/R").SaveInt("PuzzleDoor_locked-53--34", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/J").SaveInt("PuzzleDoor_locked-42--18", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/D").SaveInt("PuzzleDoor_locked-46--6", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AP").SaveInt("PuzzleDoor_locked-76--78", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/K").SaveInt("PuzzleDoor_locked-46--19", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/X").SaveInt("PuzzleDoor_locked-53--46", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/X").SaveInt("PuzzleDoor_locked-52--38", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/D").SaveInt("PuzzleDoor_locked-53--10", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/P").SaveInt("PuzzleDoor_locked-23--34", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/O").SaveInt("PuzzleDoor_locked-13--29", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AC").SaveInt("PuzzleDoor_locked-53--58", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AC").SaveInt("PuzzleDoor_locked-52--50", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/V").SaveInt("PuzzleDoor_locked-22--37", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/AO").SaveInt("PuzzleDoor_locked-71--77", 1);
			mainSaver.GetSaver("/local/levels/TombOfSimulacrum/K").SaveInt("PuzzleDoor_locked-52--14", 1);
			// Syncope
			mainSaver.GetSaver("/local/levels/DreamDynamite/N").SaveInt("PuzzleDoor_locked-97--34", 1);
			mainSaver.GetSaver("/local/levels/DreamDynamite/AU").SaveInt("PuzzleDoor_locked-61--78", 1);
			mainSaver.GetSaver("/local/levels/DreamDynamite/AT").SaveInt("PuzzleDoor_locked-58--77", 1);
			mainSaver.GetSaver("/local/levels/DreamDynamite/R").SaveInt("PuzzleDoor_locked-52--34", 1);
			// Antigram
			mainSaver.GetSaver("/local/levels/DreamFireChain/P").SaveInt("PuzzleDoor_locked-19--54", 1);
			mainSaver.GetSaver("/local/levels/DreamFireChain/L").SaveInt("PuzzleDoor_locked-20--50", 1);
			mainSaver.GetSaver("/local/levels/DreamFireChain/I").SaveInt("PuzzleDoor_locked-58--34", 1);
			mainSaver.GetSaver("/local/levels/DreamFireChain/E").SaveInt("PuzzleDoor_locked-37--15", 1);
			mainSaver.GetSaver("/local/levels/DreamFireChain/G").SaveInt("PuzzleDoor_locked-16--35", 1);
			mainSaver.GetSaver("/local/levels/DreamFireChain/F").SaveInt("PuzzleDoor_locked-13--34", 1);
			mainSaver.GetSaver("/local/levels/DreamFireChain/J").SaveInt("PuzzleDoor_locked-61--35", 1);
			// Bottomless Tower
			mainSaver.GetSaver("/local/levels/DreamIce/AC").SaveInt("PuzzleDoor_locked-64--73", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/O").SaveInt("PuzzleDoor_locked-39--25", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/T").SaveInt("PuzzleDoor_locked-17--54", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/I").SaveInt("PuzzleDoor_locked-40--22", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/R").SaveInt("PuzzleDoor_locked-99--26", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/L").SaveInt("PuzzleDoor_locked-100--22", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/V").SaveInt("PuzzleDoor_locked-65--70", 1);
			mainSaver.GetSaver("/local/levels/DreamIce/S").SaveInt("PuzzleDoor_locked-13--53", 1);
			// Quietus
			mainSaver.GetSaver("/local/levels/DreamAll/M").SaveInt("PuzzleDoor_locked-25--15", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/S").SaveInt("PuzzleDoor_locked-19--37", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/I").SaveInt("PuzzleDoor_locked-56--11", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/U").SaveInt("PuzzleDoor_locked-51--37", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/X").SaveInt("PuzzleDoor_locked-18--41", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/O").SaveInt("PuzzleDoor_locked-55--15", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/G").SaveInt("PuzzleDoor_locked-26--11", 1);
			mainSaver.GetSaver("/local/levels/DreamAll/Z").SaveInt("PuzzleDoor_locked-50--41", 1);
		}

		private void UnlockWarpGarden()
		{
			List<string> warpLetters = new() { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J" };

			for (int i = 0; i < warpLetters.Count; i++)
			{
				string warp = "WorldWarp" + warpLetters[i];
				mainSaver.WorldStorage.SaveInt(warp, 1);
				mainSaver.GetSaver("/local/markers").SaveData(warp, warp);
			}
		}

		private void ObtainedTracker3()
		{
			Dictionary<string, string[]> scenesAndRooms = new()
			{
				{ "FluffyFields", new string[] { "A", "B", "C" } },
				{ "CandyCoast", new string[] { "A", "B", "C" } },
				{ "FancyRuins", new string[] { "C", "B", "A" } },
				{ "FancyRuins2", new string[] { "A" } },
				{ "StarWoods", new string[] { "A", "B", "C" } },
				{ "StarWoods2", new string[] { "A" } },
				{ "SlipperySlope", new string[] { "A", "B" } },
				{ "VitaminHills", new string[] { "A", "B", "C" } },
				{ "VitaminHills2", new string[] { "A" } },
				{ "VitaminHills3", new string[] { "A" } },
				{ "FrozenCourt", new string[] { "A" } },
				{ "LonelyRoad", new string[] { "A", "B", "C", "D", "E" } },
				{ "LonelyRoad2", new string[] { "A" } },
				{ "Deep2", new string[] { "A", "B", "C", "D" } },
				{ "Deep3", new string[] { "B", "A" } },
				{ "Deep4", new string[] { "A", "B" } },
				{ "Deep5", new string[] { "A", "B", "C", "D", "E", "F", "G" } },
				{ "Deep6", new string[] { "B", "A" } },
				{ "Deep7", new string[] { "A", "B" } },
				{ "Deep8", new string[] { "A", "B", "C", "D" } },
				{ "Deep9", new string[] { "B", "A" } },
				{ "Deep10", new string[] { "B", "A" } },
				{ "Deep11", new string[] { "A", "B", "C", "D", "E", "F" } },
				{ "Deep12", new string[] { "A", "B", "C" } },
				{ "Deep13", new string[] { "A", "B", "C", "D", "E" } },
				{ "Deep14", new string[] { "A", "B", "C", "D", "E", "F" } },
				{ "Deep15", new string[] { "B", "A" } },
				{ "Deep17", new string[] { "A", "B" } },
				{ "Deep19s", new string[] { "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T" } },
				{ "Deep20", new string[] { "A", "B" } },
				{ "Deep22", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P" } },
				{ "PillowFort", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" } },
				{ "SandCastle", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" } },
				{ "ArtExhibit", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S" } },
				{ "TrashCave", new string[] { "J", "A", "B", "C", "D", "E", "F", "G", "H", "I", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T" } },
				{ "FloodedBasement", new string[] { "M", "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "N", "O", "P", "Q", "R", "S", "T", "U" } },
				{ "PotassiumMine", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U" } },
				{ "BoilingGrave", new string[] { "V", "A", "AA", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "W", "X", "Y", "Z" } },
				{ "GrandLibrary", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "AA", "AB", "AC", "AD", "AE" } },
				{ "GrandLibrary2", new string[] { "BA", "BB", "CA", "CB", "CC", "CD", "CE", "CF", "CG", "CH", "CI", "CJ", "CK", "CL", "CM", "CN", "CO", "CP", "CQ", "CR", "CS", "CT", "CU", "CV", "CW", "CX", "DA", "DB", "DC", "DD", "DE", "DF", "DG", "DH", "EA" } },
				{ "SunkenLabyrinth", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U" } },
				{ "MachineFortress", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R" } },
				{ "DarkHypostyle", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W" } },
				{ "TombOfSimulacrum", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "AA", "AB", "AC", "AD", "AE", "AF", "AG", "AH", "AI", "AJ", "AK", "AL", "AM", "AN", "AO", "AP", "AQ" } },
				{ "DreamForce", new string[] { "B", "C", "E", "I", "J", "K", "L", "M", "Y", "W", "X", "Z", "AA", "AE", "AF", "AG", "AH", "AD" } },
				{ "DreamDynamite", new string[] { "A", "B", "C", "D", "E", "F", "I", "K", "L", "N", "R", "V", "W", "X", "Y", "Z", "AB", "AF", "AG", "AH", "AI", "AL", "AM", "AN", "AO", "AS", "AT", "AU" } },
				{ "DreamFireChain", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R" } },
				{ "DreamIce", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "AA", "Z", "AB", "AC", "AD", "AE" } },
				{ "DreamAll", new string[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z", "AA", "AB", "AC", "AD" } },
			};

			// Mark all rooms as visited
			foreach (KeyValuePair<string, string[]> sceneAndRoom in scenesAndRooms)
			{
				for (int i = 0; i < sceneAndRoom.Value.Length; i++)
				{
					mainSaver.GetSaver($"/local/levels/{sceneAndRoom.Key}/player/seenrooms").SaveInt(sceneAndRoom.Value[i], 1);
				}
			}
		}

		private void OnPlayerSpawn(Entity player, GameObject camera, PlayerController controller)
		{
			this.player = player;
		}

		private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
		{
			if (scene.name != "MainMenu")
				return;

			itemMessageHandler.HideMessageBoxes();
			Destroy(gameObject);
		}

		private struct LocationData
		{
			public List<Location> Locations { get; set; }

			public class Location
			{
				[JsonProperty("location")]
				public string LocationName { get; set; }
				public int Offset { get; set; }
				[JsonProperty("scene")]
				public string SceneName { get; set; }
				public string Flag { get; set; }
			}
		}

		public class APFileData
		{
			public string Server { get; set; }
			public int Port { get; set; }
			public string SlotName { get; set; }
			public string Password { get; set; }
			public bool Deathlink { get; set; }
			public bool AutoEquipOutfits { get; set; }
			public bool StackStatuses { get; set; }
		}
	}
}