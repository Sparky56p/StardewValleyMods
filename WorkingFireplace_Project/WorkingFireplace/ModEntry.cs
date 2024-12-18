﻿//Copyright (c) 2020 Jahangmar

//This program is free software: you can redistribute it and/or modify
//it under the terms of the GNU Lesser General Public License as published by
//the Free Software Foundation, either version 3 of the License, or
//(at your option) any later version.

//This program is distributed in the hope that it will be useful,
//but WITHOUT ANY WARRANTY; without even the implied warranty of
//MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//GNU Lesser General Public License for more details.

//You should have received a copy of the GNU Lesser General Public License
//along with this program. If not, see <https://www.gnu.org/licenses/>.

using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.Characters;
using StardewValley.BellsAndWhistles;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;
using System.Runtime.Intrinsics.X86;
using Object = StardewValley.Object;
using xTile.Tiles;

#if Harmony
using Harmony;
using System.Threading;
using System.Runtime.CompilerServices;
#endif

//using StardewValley.Menus;
//using System.Collections.Generic;

//TODO: implement loading of old save state

namespace WorkingFireplace
{
    public class ModEntry : Mod
    {
        private WorkingFireplaceConfig Config;

        private const double defaultYesterdayCOFLow = 1000;
        private const string COFModID = "KoihimeNakamura.ClimatesOfFerngill";

        private double yesterdayCOFLow = defaultYesterdayCOFLow;
        private bool tooColdToday = false;
        private double tempToday = defaultYesterdayCOFLow;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<WorkingFireplaceConfig>();

#if !Harmony
            helper.Events.Input.ButtonPressed += Input_ButtonPressed;
#else
            Monitor.Log("This is the android version of the mod using harmony", LogLevel.Debug);
            FurniturePatch.Initialize(Monitor, Config, Helper);
            ApplyHarmonyPatches();
#endif
            helper.Events.GameLoop.DayStarted += GameLoop_DayStarted;
            helper.Events.Display.MenuChanged += Display_MenuChanged;
        }

#if Harmony
        /// <summary>Apply harmony patches for turning the fireplace on and off.</summary>
        private void ApplyHarmonyPatches()
        {
            // create a new Harmony instance for patching source code
            HarmonyInstance harmony = HarmonyInstance.Create(this.ModManifest.UniqueID);

            // apply the patch
            harmony.Patch(
                original: AccessTools.Method(typeof(StardewValley.Objects.Furniture), "checkForAction"),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(FurniturePatch), nameof(FurniturePatch.checkForAction_Prefix)))
            );
        }

        internal class FurniturePatch
        {
            private static IMonitor Monitor;
            private static WorkingFireplaceConfig Config;
            private static IModHelper Helper;

            // call this method from the Entry class
            public static void Initialize(IMonitor monitor, WorkingFireplaceConfig config, IModHelper helper)
            {
                Monitor = monitor;
                Config = config;
                Helper = helper;
            }


            /// <summary>This code replaces game code, it runs when the game checks for possible player actions on an item of furniture.</summary>
            /// <param name="who">The current player.</param>
            /// <param name="__instance">Furniture object the player is interacting with.</param>
            /// <returns>If not a fireplace, returns true (This means the actual game code will run). If a fireplace set to off, with sufficient wood to light a fire, it will subtract wood from inventory and return true (Game code will run). If a fireplace set to on, or insufficient wood to light a fire, return false (Actual game code will not run.)</returns>
            internal static bool checkForAction_Prefix(Farmer who, StardewValley.Objects.Furniture __instance)
            {
                try
                {
                    if (__instance.furniture_type == Furniture.fireplace) // is Fireplace
                    {
                        if (__instance.isOn) // fireplace is on
                        {
                            Monitor.Log("Action to turn fireplace off was suppressed.", LogLevel.Trace);
                            return false; // Suppress action to turn the fireplace off.
                        }
                        else
                        {
                            Item item = who.CurrentItem;
                            if (item != null && item.Name == "Wood" && item.Stack >= Config.wood_pieces) // At least X wood in inventory
                            {
                                who.removeItemsFromInventory(item.ParentSheetIndex, Config.wood_pieces);
                                Monitor.Log("Fireplace turned on.", LogLevel.Trace);
                                return true; // Allow action to light the fireplace.
                            }
                            else
                            {
                                Game1.showRedMessage(Helper.Translation.Get("msg.nowood", new { Config.wood_pieces }));
                                Monitor.Log("No wood; fireplace could not be turned on.", LogLevel.Trace);
                                return false; // Insufficient wood; suppress action to light the fireplace.
                            }
                        }
                    }
                    else
                    {
                        return true; // Not a fireplace; run original logic
                    }
                }
                catch (Exception ex)
                {
                    Monitor.Log($"Failed in {nameof(checkForAction_Prefix)}:\n{ex}", LogLevel.Error);
                    return true; // run original logic
                }
            }
        }
#endif

            void Display_MenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is StardewValley.Menus.DialogueBox && Game1.player.isInBed.Value && Config.show_temperature_in_bed)
            {
                Helper.Events.Display.RenderedActiveMenu += Display_RenderedActiveMenu;
            }
            else
            {
                Helper.Events.Display.RenderedActiveMenu -= Display_RenderedActiveMenu;
            }
        }

        void Display_RenderedActiveMenu(object sender, RenderedActiveMenuEventArgs e)
        {
            //from Game1.drawHUD
            float num = 0.625f;
            Rectangle rectangle = Game1.graphics.GraphicsDevice.Viewport.GetTitleSafeArea();
            float x = (float)(rectangle.Right - 48 - 8);
            rectangle = Game1.graphics.GraphicsDevice.Viewport.GetTitleSafeArea();
            Vector2 vector = new(x, (float)(rectangle.Bottom - 224 - 16 - (int)((float)(Game1.player.MaxStamina - 270) * num)));

            bool isCold = !WarmInside(false) && tooColdToday;

            string text = Helper.Translation.Get("msg.temp") + ((isCold ? Helper.Translation.Get("msg.tempcold") : Helper.Translation.Get("msg.tempwarm")));

            int width = SpriteText.getWidthOfString(text);
            SpriteText.drawString(e.SpriteBatch, text, (int)vector.X - width, (int)vector.Y - 100, 999999, -1, 999999, 1f, 0.88f, false, -1, "", isCold ? SpriteText.color_Cyan : SpriteText.color_Orange);
        }


        void GameLoop_DayStarted(object sender, DayStartedEventArgs e)
        {

            SetTemperatureToday();

            bool warmth = WarmInside(true);


            bool tooColdOutside = TooColdYesterday();

            if (tooColdOutside)
            {
                if (warmth)
                {
                    if (Config.showMessageOnStartOfDay)
                        Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.warm")));
                }
                else
                {
                    if (Config.showMessageOnStartOfDay)
                    {
                        if (HasSpouse())
                            Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.spousecold")));
                        else
                            Game1.addHUDMessage(new HUDMessage(Helper.Translation.Get("msg.cold")));
                    }
                    Game1.currentLocation.playSound("coldSpell");

                    if (Config.penalty)
                    {
                        Game1.player.health = CalcAttribute(Game1.player.health, Config.reduce_health, Game1.player.maxHealth);
                        Game1.player.stamina = CalcAttribute(Game1.player.stamina, Config.reduce_stamina, Game1.player.maxStamina.Value);
                        if (HasSpouse())
                            Game1.player.changeFriendship(-Config.reduce_friendship_spouse, GetSpouse());
                        Game1.player.getChildren().ForEach((child) => Game1.player.changeFriendship(-Config.reduce_friendship_children, child));
                    }

                    if (HasSpouse())
                    {
                        string please = Helper.Translation.Get("dia.please");
                        switch (Game1.player.getChildrenCount())
                        {
                            case 1:
                                Child child = Game1.player.getChildren()[0];
                                GetSpouse().setNewDialogue("$2" + Helper.Translation.Get("dia.spousecold_child", new { child1 = child.Name }) + " " + please, true);
                                break;
                            case 2:
                                Child child1 = Game1.player.getChildren()[0];
                                Child child2 = Game1.player.getChildren()[1];
                                GetSpouse().setNewDialogue("$2" + Helper.Translation.Get("dia.spousecold_children", new { child1 = child1.Name, child2 = child2.Name }) + " " + please, true);
                                break;
                            default:
                                GetSpouse().setNewDialogue("$2" + Helper.Translation.Get("dia.spousecold") + " " + please, true);
                                break;
                        }
                    }

                }
            }
        }

        private bool HasSpouse() => GetSpouse() != null;
        private NPC GetSpouse() => Game1.player.getSpouse();

        void Input_ButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            Point grabtile = VectorToPoint(e.Cursor.GrabTile);

            if (Game1.currentLocation is FarmHouse farmHouse && Game1.activeClickableMenu == null &&
                e.Button.IsUseToolButton() && e.IsDown(e.Button))
            {
                //the fireplace is moved. We want to turn it off to avoid floating flames.
                Point tile = VectorToPoint(Game1.player.GetGrabTile());
                SetFireplace(farmHouse, tile.X, tile.Y, false, true);
            }
            else if (Game1.currentLocation is FarmHouse farmHouse1 && Game1.activeClickableMenu == null &&
                e.Button.IsActionButton() && e.IsDown(e.Button) &&
                farmHouse1.getObjectAtTile(grabtile.X, grabtile.Y) is Furniture furniture &&
                furniture.furniture_type.Value == Furniture.fireplace)
            {
                Helper.Input.Suppress(e.Button);

                if (!furniture.IsOn)
                {
                    Item item = Game1.player.CurrentItem;
                    if (item != null && item.Name == "Wood" && item.Stack >= Config.wood_pieces)
                    {
                        Game1.player.ActiveItem.ConsumeStack(Config.wood_pieces);
                        SetFireplace(farmHouse1, grabtile.X, grabtile.Y, true);
                        return;
                    }
                    Game1.showRedMessage(Helper.Translation.Get("msg.nowood", new { Config.wood_pieces }));
                }
            }

            else if (Game1.currentLocation is FarmHouse farmHouse2 && Game1.activeClickableMenu == null &&
                farmHouse2.getObjectAtTile(grabtile.X, grabtile.Y) == null)
            {
                foreach (Furniture furniture1 in farmHouse2.furniture)
                {
                    if (e.Button.IsActionButton() && e.IsDown(e.Button) && furniture1.furniture_type.Value == Furniture.fireplace && !Game1.player.IsSitting() && 
                        Utility.tileWithinRadiusOfPlayer((int)furniture1.TileLocation.X, (int)furniture1.TileLocation.Y, 1, Game1.player) | Utility.tileWithinRadiusOfPlayer((int)furniture1.TileLocation.X+1, (int)furniture1.TileLocation.Y, 1, Game1.player))
                    {
                        Helper.Input.Suppress(e.Button);
                        Game1.showRedMessage("Debug - Null Range");
                    }
                    else if (Game1.currentLocation is FarmHouse farmHouse4 && Game1.activeClickableMenu == null &&
               e.Button.IsUseToolButton() && e.IsDown(e.Button))
                    {
                        //the fireplace is moved. We want to turn it off to avoid floating flames.
                        Point tile = VectorToPoint(Game1.player.GetGrabTile());
                        SetFireplace(farmHouse4, tile.X, tile.Y, false, true);
                    }
                }
            }
        }
        private bool WarmInside(bool changeFireplace)
        {
            bool warmth = false;

            if (Config.warm_on_day_one && Game1.Date.TotalDays == 0)
                warmth = true;

            //Monitor.Log("Total days: " + Game1.Date.TotalDays);

            if (Game1.currentLocation is FarmHouse farmHouse)
            {
                foreach (Furniture furniture in farmHouse.furniture)
                {
                    if (furniture.furniture_type.Value == Furniture.fireplace && furniture.IsOn)
                    {
                        Point tile = VectorToPoint(furniture.TileLocation);
                        if (changeFireplace)
                            SetFireplace(farmHouse, tile.X, tile.Y, false, false);
                        warmth = true;
                    }
                }
            }
            else
            {
                warmth = true; //if the player sleeps somewhere else (Sleepover mod)
            }
            return warmth;
        }

        private bool TooColdYesterday()
        {
            bool tooColdOutside = false;
            if (Config.COFIntegration && Helper.ModRegistry.IsLoaded(COFModID)) //check if ClimatesOfFerngill is loaded and integration is activated
            {
                double todayCOFLow = Helper.Reflection.GetMethod(Helper.ModRegistry.GetApi(COFModID), "GetTodaysLow").Invoke<double>(Array.Empty<object>());
                tooColdOutside = (((int)yesterdayCOFLow == (int)defaultYesterdayCOFLow) ? todayCOFLow : yesterdayCOFLow) - (Game1.wasRainingYesterday ? Config.COFRainImpact : 0) <= Config.COFMinTemp;
                Monitor.Log("Climates of Ferngill integration is active. Temperature is " + (tooColdOutside ? "cold" : "warm"), LogLevel.Trace);
                yesterdayCOFLow = todayCOFLow;
                Monitor.Log("yesterdayCOFLow set to " + yesterdayCOFLow, LogLevel.Trace);

            }
            else
            {
                tooColdOutside = Game1.IsWinter && (Config.need_fire_in_winter || Game1.wasRainingYesterday && Config.need_fire_in_winter_rain) ||
                                 Game1.IsSpring && (Config.need_fire_in_spring || Game1.wasRainingYesterday && Config.need_fire_in_spring_rain) ||
                                 Game1.IsSummer && (Config.need_fire_in_summer || Game1.wasRainingYesterday && Config.need_fire_in_summer_rain) ||
                                 Game1.IsFall && (Config.need_fire_in_fall || Game1.wasRainingYesterday && Config.need_fire_in_fall_rain);

                Monitor.Log("Temperature is " + (tooColdOutside ? "cold" : "warm"), LogLevel.Trace);
            }
            return tooColdOutside;
        }

        private void SetTemperatureToday()
        {
            bool tooColdOutside = false;
            if (Config.COFIntegration && Helper.ModRegistry.IsLoaded(COFModID)) //check if ClimatesOfFerngill is loaded and integration is activated
            {
                double todayCOFLow = Helper.Reflection.GetMethod(Helper.ModRegistry.GetApi(COFModID), "GetTodaysLow").Invoke<double>(Array.Empty<object>());
                tooColdOutside = todayCOFLow - (Game1.isRaining ? Config.COFRainImpact : 0) <= Config.COFMinTemp;
                tempToday = todayCOFLow;

            }
            else
            {
                tooColdOutside = Game1.IsWinter && (Config.need_fire_in_winter || Game1.isRaining && Config.need_fire_in_winter_rain) ||
                                 Game1.IsSpring && (Config.need_fire_in_spring || Game1.isRaining && Config.need_fire_in_spring_rain) ||
                                 Game1.IsSummer && (Config.need_fire_in_summer || Game1.isRaining && Config.need_fire_in_summer_rain) ||
                                 Game1.IsFall && (Config.need_fire_in_fall || Game1.isRaining && Config.need_fire_in_fall_rain);
            }
            tooColdToday = tooColdOutside;
        }

        private int CalcAttribute(float value, double fac, int max)
        {
            int result = Convert.ToInt32(value - max * fac);

            if (result > max)
                return max;
            else if (result <= 0)
                return 1;
            else
                return result;
        }

        /// <summary>
        /// Checks if the given position matches a fireplace.
        /// Toggles the fireplace on or off if its state differs from <c>on</c>./// 
        /// </summary>
        /// <param name="farmHouse">Farm house.</param>
        /// <param name="X">X tile position of fireplace.</param>
        /// <param name="Y">Y tile position of fireplace.</param>
        /// <param name="on">new state of fireplace.</param>
        /// <param name="playsound">should a sound be played?</param>
        private void SetFireplace(FarmHouse farmHouse, int X, int Y, bool on, bool playsound = true)
        {
            if (farmHouse.getObjectAtTile(X, Y) is Furniture furniture && furniture.furniture_type.Value == Furniture.fireplace)
            {
                //fireplaces are two tiles wide. The "FarmHouse.setFireplace" method needs the left tile so we set it to the left one.
                if (farmHouse.getObjectAtTile(X-1, Y) == furniture)
                {
                    X = X - 1;
                }
                if (furniture.IsOn != on)
                {
                    furniture.isOn.Set(on);
                    farmHouse.setFireplace(on, X, Y, playsound);
                    if (!on && furniture.lightSource != null)
                    {
                        farmHouse.removeLightSource(furniture.lightSource.Id);
                    }
                }
            }
        }

        private Point VectorToPoint(Vector2 vec)
        {
            return new Point(Convert.ToInt32(vec.X), Convert.ToInt32(vec.Y));
        }

    }
}
