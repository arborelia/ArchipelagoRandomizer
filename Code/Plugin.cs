﻿using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ModCore;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace ArchipelagoRandomizer
{
	[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	[BepInDependency("ModCore")]
	public class Plugin : BaseUnityPlugin
	{
		internal static Plugin Instance { get; private set; }
		internal static ManualLogSource Log { get; private set; }

		private APHandler apHandler;
		private ItemRandomizer itemRandomizer;
		private APCommand apCommandHandler;

		private void Awake()
		{
			Instance = this;
			Log = Logger;
			Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

			apHandler = new APHandler();
			itemRandomizer = new ItemRandomizer();
			apCommandHandler = new APCommand();
			apCommandHandler.AddCommands();
			DebugMenuManager.LogToConsole("To connect to an ArchipelagoHandler server, use 'ap /connect {server:port} {slot} {password}");

			Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly());

			Events.OnFileStart += (newFile) =>
			{
				itemRandomizer.SetupNewFile(newFile);
			};
		}

		public static Coroutine StartRoutine(IEnumerator coroutine)
		{
			return Instance.StartCoroutine(coroutine);
		}
	}
}