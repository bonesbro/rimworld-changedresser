﻿using ChangeDresser.UI.Util;
using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ChangeDresser
{
    [StaticConstructorOnStartup]
    class Main
    {
        static Main()
        {
            var harmony = HarmonyInstance.Create("com.changedresser.rimworld.mod");
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            WidgetUtil.Initialize();
            
            Log.Message("ChangeDresser: Adding Harmony Postfix to Pawn.GetGizmos");
            //Log.Message("ChangeDresser: Adding Harmony Postfix to Pawn_ApparelTracker.Notify_ApparelAdded");
            Log.Message("ChangeDresser: Adding Harmony Postfix to Pawn_DraftController.Drafted { set }");
            Log.Message("ChangeDresser: Adding Harmony Postfix to Pawn_DraftController.GetGizmos");
            Log.Message("ChangeDresser: Adding Harmony Postfix to JobGiver_OptimizeApparel.TryGiveJob(Pawn)");
            Log.Message("ChangeDresser: Adding Harmony Postfix to ReservationManager.CanReserve");
            Log.Message("ChangeDresser: Adding Harmony Postfix to OutfitDatabase.TryDelete");
        }

        public static Texture2D GetIcon(ThingDef td)
        {
            Texture2D tex = null;
            if (td.uiIcon != null)
            {
                tex = td.uiIcon;
            }
            else if (td?.graphicData?.texPath != null)
            {
                tex = ContentFinder<Texture2D>.Get(td.graphicData.texPath, true);
            }
            else
            {
                tex = null;
            }

            if (tex == null)
            {
                tex = WidgetUtil.noneTexture;
            }

            return tex;
        }

        public static void SwapApparel(Pawn pawn, Outfit toWear)
        {
#if DEBUG
            Log.Message(
                Environment.NewLine + 
                "Start Main.SwapApparel Pawn: " + pawn.Name.ToStringShort + " toWear: " + toWear.label);
#endif
            // Remove apparel from pawn
            List<Apparel> worn = new List<Apparel>(pawn.apparel.WornApparel);
            foreach (Apparel a in worn)
            {
                if (Settings.KeepForcedApparel && 
                    pawn.outfits.forcedHandler.ForcedApparel.Contains(a))
                {
                    continue;
                }
                    
                pawn.apparel.Remove(a);
#if DEBUG
                Log.Warning(" Apparel " + a.LabelShort + " removed");
#endif

                /*bool handled = false;
                foreach (Building_Dresser d in WorldComp.DressersToUse)
                {
#if DEBUG
                    Log.Warning("  Dresser " + d.Label);
#endif
                    if (d.settings.filter.Allows(a))
                    {
#if DEBUG
                        Log.Warning("   Does Handle");
#endif
                        d.AddApparel(a);
                        handled = true;
                        break;
                    }
#if DEBUG
                    else
                    {
                        Log.Warning("   Does Not Handle");
                    }
#endif
                }*/
                if (!WorldComp.AddApparel(a))
                {
#if DEBUG
                    Log.Warning("  Apparel " + a.LabelShort + " was not handled");
#endif
                    Thing t;
                    if (!a.Spawned)
                    {
                        GenThing.TryDropAndSetForbidden(a, pawn.Position, pawn.Map, ThingPlaceMode.Near, out t, false);
                        if (!a.Spawned)
                        {
                            GenPlace.TryPlaceThing(a, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                        }
                    }
                }
            }

            pawn.outfits.CurrentOutfit = toWear;

            typeof (JobGiver_OptimizeApparel)
                .GetField("neededWarmth", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, PawnApparelGenerator.CalculateNeededWarmth(pawn, pawn.Map.Tile, GenLocalDate.Twelfth(pawn)));

            MethodInfo mi = typeof(JobGiver_OptimizeApparel).GetMethod("TryGiveJob", BindingFlags.Instance | BindingFlags.NonPublic);

            JobGiver_OptimizeApparel apparelOptimizer = new JobGiver_OptimizeApparel();
            object[] param = new object[] { pawn };
            for (int i = 0; i < 10; ++i)
            {
#if DEBUG
                Log.Warning(i + " start equip for loop");
#endif
                Job job = mi.Invoke(apparelOptimizer, param) as Job;
#if DEBUG
                Log.Warning(i + " job is null: " + (string)((job == null) ? "yes" : "no"));
#endif
                if (job == null)
                    break;
#if DEBUG
                Log.Warning(job.def.defName);
#endif
                if (job.def == JobDefOf.Wear)
                {
                    Apparel a = ((job.targetB != null) ? job.targetB.Thing : null) as Apparel;
                    if (a == null)
                    {
                        Log.Warning("ChangeDresser: Problem equiping pawn. Apparel is null.");
                        break;
                    }
#if DEBUG
                    Log.Warning("Wear from ground " + a.Label);
#endif
                    pawn.apparel.Wear(a);
                }
                else if (job.def == Building_Dresser.WEAR_APPAREL_FROM_DRESSER_JOB_DEF)
                {
                    Building_Dresser d = ((job.targetA != null) ? job.targetA.Thing : null) as Building_Dresser;
                    Apparel a = ((job.targetB != null) ? job.targetB.Thing : null) as Apparel;

                    if (d == null || a == null)
                    {
                        Log.Warning("ChangeDresser: Problem equiping pawn. Dresser or Apparel is null.");
                        break;
                    }
#if DEBUG
                    Log.Warning("Wear from dresser " + d.Label + " " + a.Label);
#endif
                    d.RemoveNoDrop(a);
                    pawn.apparel.Wear(a);
                }
#if DEBUG
                Log.Warning(i + " end equip for loop");
#endif
            }

            if (pawn.apparel.WornApparelCount == 0)
            {
                // When pawns are not on the home map they will not get dressed using the game's normal method

                // This logic works but pawns will run back to the dresser to change cloths
                foreach (ThingDef def in toWear.filter.AllowedThingDefs)
                {
    #if DEBUG
                    Log.Warning("  Try Find Def " + def.label);
    #endif
                    if (pawn.apparel.CanWearWithoutDroppingAnything(def))
                    {
    #if DEBUG
                        Log.Warning("   Can wear");
    #endif
                        foreach (Building_Dresser d in WorldComp.DressersToUse)
                        {
    #if DEBUG
                            Log.Warning("   Check dresser " + d.Label);
    #endif
                            Apparel apparel;
                            if (d.TryRemoveBestApparel(def, toWear.filter, out apparel))
                            {
    #if DEBUG
                                Log.Warning("    Found " + apparel.LabelShort);
    #endif
                                pawn.apparel.Wear(apparel);
                                break;
                            }
    #if DEBUG
                            else
                                Log.Warning("    No matching apparel found");
    #endif
                        }
                    }
    #if DEBUG
                    else
                        Log.Warning("  Can't wear");
    #endif
                }
            }
#if DEBUG
            Log.Message("End Main.SwapApparel" + Environment.NewLine);
#endif
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    static class Pawn_GetGizmos
    {
#if DEBUG
        private static int i = 0;
        private static readonly int WAIT = 1000;
#endif
        static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (!__instance.Drafted)
            {
#if DEBUG
                ++i;
                if (i == WAIT)
                    Log.Warning("DraftController.Postfix: Pawn is Drafted");
#endif
                PawnOutfits outfits;
                if (WorldComp.PawnOutfits.TryGetValue(__instance, out outfits))
                {
                    List<Gizmo> l = new List<Gizmo>(__result);
#if DEBUG
                    if (i == WAIT)
                        Log.Warning("DraftController.Postfix: Sets found! Pre Gizmo Count: " + l.Count);
#endif
                    foreach (Outfit o in outfits.Outfits)
                    {
                        if (o == null)
                            continue;
                        bool forBattle = WorldComp.OutfitsForBattle.Contains(o);
#if DEBUG
                        if (i == WAIT)
                            Log.Warning("DraftController.Postfix: Set: " + o.label + ", forBattle: " + forBattle + ", Cuurent Oufit: " + __instance.outfits.CurrentOutfit.label);
#endif
                        if (!forBattle)
                        {
                            Command_Action a = new Command_Action();
                            List<ThingDef> tdList = new List<ThingDef>(o.filter.AllowedThingDefs);
                            if (tdList.Count > 0)
                            {
                                a.icon = Main.GetIcon(tdList[0]);
                            }
                            else
                            {
                                a.icon = WidgetUtil.noneTexture;
                            }
                            StringBuilder sb = new StringBuilder();
                            if (!__instance.outfits.CurrentOutfit.Equals(o))
                            {
                                sb.Append("ChangeDresser.ChangeTo".Translate());
                                a.defaultDesc = "ChangeDresser.ChangeToDesc".Translate();
                            }
                            else
                            {
                                sb.Append("ChangeDresser.Wearing".Translate());
                                a.defaultDesc = "ChangeDresser.WearingDesc".Translate();
                            }
                            sb.Append(" ");
                            sb.Append(o.label);
                            a.defaultLabel = sb.ToString();
                            a.activateSound = SoundDef.Named("Click");
                            a.action = delegate
                            {
                                Main.SwapApparel(__instance, o);
                            };
                            l.Add(a);
                        }
                    }
#if DEBUG
                    if (i == WAIT)
                        Log.Warning("Post Gizmo Count: " + l.Count);
#endif
                    __result = l;
                }
            }
#if DEBUG
            else
            {
                if (i == WAIT)
                    Log.Warning("Pawn is not Drafted, could gizmo");
            }
#endif
#if DEBUG
            if (i == WAIT)
                i = 0;
#endif
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), "GetGizmos")]
    static class Patch_Pawn_DraftController_GetGizmos
    {
#if DEBUG
        private static int i = 0;
        private static readonly int WAIT = 1000;
#endif
        static void Postfix(Pawn_DraftController __instance, ref IEnumerable<Gizmo> __result)
        {
            Pawn pawn = __instance.pawn;
            if (pawn.Drafted)
            {
#if DEBUG
                ++i;
                if (i == WAIT)
                    Log.Warning("DraftController.Postfix: Pawn is Drafted");
#endif
                PawnOutfits outfits;
                if (WorldComp.PawnOutfits.TryGetValue(pawn, out outfits))
                {
                    List<Gizmo> l = new List<Gizmo>(__result);
#if DEBUG
                    if (i == WAIT)
                        Log.Warning("DraftController.Postfix: Sets found! Pre Gizmo Count: " + l.Count);
#endif
                    foreach (Outfit o in outfits.Outfits)
                    {
                        if (o == null)
                            continue;
                        bool forBattle = WorldComp.OutfitsForBattle.Contains(o);
#if DEBUG
                        if (i == WAIT)
                            Log.Warning("DraftController.Postfix: Set: " + o.label + ", forBattle: " + forBattle + ", Current Oufit: " + pawn.outfits.CurrentOutfit.label);
#endif
                        if (forBattle)
                        {
                            Command_Action a = new Command_Action();
                            List<ThingDef> tdList = new List<ThingDef>(o.filter.AllowedThingDefs);
                            if (tdList.Count > 0)
                            {
                                a.icon = Main.GetIcon(tdList[0]);
                            }
                            else
                            {
                                a.icon = WidgetUtil.noneTexture;
                            }
                            StringBuilder sb = new StringBuilder();
                            if (!pawn.outfits.CurrentOutfit.Equals(o))
                            {
                                sb.Append("ChangeDresser.ChangeTo".Translate());
                                a.defaultDesc = "ChangeDresser.ChangeToDesc".Translate();
                            }
                            else
                            {
                                sb.Append("ChangeDresser.Wearing".Translate());
                                a.defaultDesc = "ChangeDresser.WearingDesc".Translate();
                            }
                            sb.Append(" ");
                            sb.Append(o.label);
                            a.defaultLabel = sb.ToString();
                            a.activateSound = SoundDef.Named("Click");
                            a.action = delegate
                            {
                                Main.SwapApparel(pawn, o);
                            };
                            l.Add(a);
                        }
                    }
#if DEBUG
                    if (i == WAIT)
                        Log.Warning("Post Gizmo Count: " + l.Count);
#endif
                    __result = l;
                }
            }
#if DEBUG
            else
            {
                if (i == WAIT)
                    Log.Warning("Pawn is not Drafted, could gizmo");
            }
#endif
#if DEBUG
            if (i == WAIT)
                i = 0;
#endif
        }
    }

    [HarmonyPatch(typeof(Pawn_DraftController), "set_Drafted")]
    static class Patch_Pawn_DraftController
    {
        static void Postfix(Pawn_DraftController __instance)
        {
            Pawn pawn = __instance.pawn;
            PawnOutfits outfits;
            if (WorldComp.PawnOutfits.TryGetValue(pawn, out outfits))
            {
                Outfit outfitToWear;
                bool found = false;
                if (pawn.Drafted)
                {
                    if (outfits.TryGetBattleOutfit(out outfitToWear))
                    {
                        outfits.LastCivilianOutfit = pawn.outfits.CurrentOutfit;
                        found = true;
                    }
                }
                else
                {
                    if (outfits.TryGetCivilianOutfit(out outfitToWear))
                    {
                        outfits.LastBattleOutfit = pawn.outfits.CurrentOutfit;
                        found = true;
                    }
                }

                if (found)
                {
                    Main.SwapApparel(pawn, outfitToWear);
                }
            }
        }
    }

    [HarmonyPatch(typeof(JobGiver_OptimizeApparel), "TryGiveJob", new Type[] { typeof(Pawn) })]
    static class Patch_JobGiver_OptimizeApparel
    {
        static void Postfix(Pawn pawn, ref Job __result)
        {
            if (!DoDressersHaveApparel())
            {
                return;
            }

            Thing thing = null;
            if (__result != null)
            {
                thing = __result.targetA.Thing;
            }

            Building_Dresser containingDresser = null;
            float baseApparelScore = 0f;

            foreach (Building_Dresser dresser in WorldComp.DressersToUse)
            {
                float score = baseApparelScore;
                Apparel a = dresser.FindBetterApparel(ref score, pawn, pawn.outfits.CurrentOutfit);

                if (score > baseApparelScore && a != null)
                {
                    thing = a;
                    baseApparelScore = score;
                    containingDresser = dresser;
                }
                
            }
            if (thing != null && containingDresser != null)
            {
                __result = new Job(containingDresser.wearApparelFromStorageJobDef, containingDresser, thing);
            }
        }

        /*public static bool TryGetBestApparel(Thing original, Pawn pawn, out Thing betterThing, out Building_Dresser containingDresser)
        {
            containingDresser = null;
            betterThing = null;
            float baseApparelScore = 0f;

            foreach (Building_Dresser dresser in WorldComp.DressersToUse)
            {
                float score = baseApparelScore;
                Apparel a = dresser.FindBetterApparel(ref score, pawn, pawn.outfits.CurrentOutfit);

                if (score > baseApparelScore && a != null)
                {
                    betterThing = a;
                    baseApparelScore = score;
                    containingDresser = dresser;
                }

            }
            return betterThing != null && containingDresser != null;
        }*/

        private static bool DoDressersHaveApparel()
        {
            foreach (Building_Dresser d in WorldComp.DressersToUse)
            {
                if (d.Count > 0)
                {
                    return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(TradeShip), "ColonyThingsWillingToBuy")]
    static class Patch_TradeShip_ColonyThingsWillingToBuy
    {
        //private static FieldInfo pawnFieldInfo = null;
        static void Postfix(ref IEnumerable<Thing> __result, Pawn playerNegotiator)
        {
            /*if (pawnFieldInfo == null)
            {
                pawnFieldInfo = typeof(Pawn_TraderTracker).GetField("pawn", BindingFlags.NonPublic | BindingFlags.Instance);
            }

            Pawn pawn = pawnFieldInfo.GetValue(__instance) as Pawn;*/

            if (playerNegotiator != null && playerNegotiator.Map != null)
            {
                List<Thing> things = new List<Thing>();
                if (__result != null)
                {
                    things.AddRange(__result);
                }
#if TRADE_DEBUG
                Log.Warning("Patch TradeShip.ColonyThingsWillingToBuy: Pawn name: " + playerNegotiator?.Name);
#endif
                foreach (Building_Dresser d in WorldComp.DressersToUse)
                {
                    if (d.IncludeInTradeDeals && d.Map == playerNegotiator.Map)
                    {
                        things.AddRange(d.EmptyOnTop());
                    }
                }
                __result = things;
            }
#if TRADE_DEBUG
            else
            {
                Log.Warning("Patch TradeShip.ColonyThingsWillingToBuy: Pawn is null");
            }
#endif
        }
    }

    [HarmonyPatch(typeof(Dialog_Trade), "Close")]
    static class Patch_Dialog_Trade_Close
    {
        static void Postfix()
        {
            foreach (Building_Dresser d in WorldComp.DressersToUse)
            {
                if (d.Map != null)
                {
                    d.HandleThingsOnTop();
                }
            }
        }
    }

    [HarmonyPatch(typeof(ReservationManager), "CanReserve")]
    static class Patch_ReservationManager_CanReserve
    {
        private static FieldInfo mapFI = null;
        static void Postfix(ref bool __result, ReservationManager __instance, Pawn claimant, LocalTargetInfo target, int maxPawns, int stackCount, ReservationLayerDef layer, bool ignoreOtherReservations)
        {
            if (mapFI == null)
            {
                mapFI = typeof(ReservationManager).GetField("map", BindingFlags.NonPublic | BindingFlags.Instance);
            }

#if DEBUG
            Log.Warning("\nCanReserve original result: " + __result);
#endif
            if (!__result && (target.Thing == null || target.Thing.def.defName.Equals("ChangeDresser")))
            {
                IEnumerable<Thing> things = ((Map)mapFI.GetValue(__instance))?.thingGrid.ThingsAt(target.Cell);
                if (things != null)
                {
#if DEBUG
                    Log.Warning("CanReserve - Found things");
#endif
                    foreach (Thing t in things)
                    {
#if DEBUG
                        Log.Warning("CanReserve - def " + t.def.defName);
#endif
                        if (t.def.defName.Equals("ChangeDresser"))
                        {
#if DEBUG
                            Log.Warning("CanReserve is now true\n");
#endif
                            __result = true;
                        }
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(OutfitDatabase), "TryDelete")]
    static class Patch_OutfitDatabase_TryDelete
    {
        static void Postfix(ref AcceptanceReport __result, Outfit outfit)
        {
            if (__result.Accepted)
            {
                WorldComp.OutfitsForBattle.Remove(outfit);
            }
        }
    }

    /*[HarmonyPatch(typeof(TradeDeal), "TryExecute")]
    static class Patch_TradeDeal_TryExecute
    {
        static void Postfix(ref bool __result)
        {
            if (__result)
            {
#if TRADE_DEBUG
                Log.Warning("Start ChangeDresser.Patch_TradeDeal_TryExecute");
#endif
                foreach (Building_Dresser d in WorldComp.DressersToUse)
                {
                    if (d.Map != null)
                    {
                        d.HandleThingsOnTop();
                    }
                }
#if TRADE_DEBUG
                Log.Warning("End ChangeDresser.Patch_TradeDeal_TryExecute");
#endif
            }
        }
    }*/

    /* This prevents pawns from constantly switching apparel
    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelAdded")]
    static class Patch_Pawn_ApparelTracker_Notify_ApparelAdded
    {
        struct LastTimeAndTries
        {
            public int Tries;
            public long LastTime;
            public LastTimeAndTries(int tries, long lastTime)
            {
                this.Tries = tries;
                this.LastTime = lastTime;
            }
        }
        static Dictionary<Pawn, LastTimeAndTries> lastTimeAndTries = new Dictionary<Pawn, LastTimeAndTries>();
        static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
#if DEBUG || DEBUG_TRACKER
            Log.Message(Environment.NewLine + "Start Pawn_ApparelTracker.Notify_ApparelAdded");
#endif
            long now = DateTime.Now.Ticks;
            LastTimeAndTries i;
            if (lastTimeAndTries.TryGetValue(__instance.pawn, out i))
            {
                long delta = now - i.LastTime;
                if (delta < TimeSpan.TicksPerMinute)
                {
                    if (i.Tries >= 8)
                    {
#if DEBUG || DEBUG_TRACKER
                        Log.Warning(__instance.pawn.Name.ToStringShort + " reached the maximum number of tried in a minute");
#endif
                        return;
                    }
                    else // i.Tries < 8
                    {
#if DEBUG || DEBUG_TRACKER
                        Log.Warning(__instance.pawn.Name.ToStringShort + " try count: " + i);
#endif
                        ++i.Tries;
                    }
                }
                else
                {
#if DEBUG || DEBUG_TRACKER
                    Log.Warning(__instance.pawn.Name.ToStringShort + " try reset");
#endif
                    i.Tries = 1;
                    i.LastTime = now;
                }
            }
            else
            {
                i = new LastTimeAndTries(1, now);
            }

            PawnOutfits po;
            if (WorldComp.PawnOutfits.TryGetValue(__instance.pawn, out po))
            {
#if DEBUG
                Log.Warning(" po found");
#endif
                Color c;
                if (po.TryGetColorFor(apparel.def.apparel.LastLayer, out c))
                {
#if DEBUG
                    Log.Warning(" assigned color for layer " + apparel.def.apparel.LastLayer);
#endif
                    CompColorableUtility.SetColor(apparel, c, true);
                    __instance.pawn.Drawer.renderer.graphics.ResolveAllGraphics();
                    PortraitsCache.SetDirty(__instance.pawn);
                }
#if DEBUG
                else
                {
                    Log.Warning(" no assigned color for layer " + apparel.def.apparel.LastLayer);
                }
#endif
            }
#if DEBUG || DEBUG_TRACKER
            Log.Message("End Pawn_ApparelTracker.Notify_ApparelAdded" + Environment.NewLine);
#endif
        }
    }*/
    /*[HarmonyPatch(typeof(Pawn_TraderTracker), "ColonyThingsWillingToBuy")]
    static class Patch_Pawn_TraderTracker_ColonyThingsWillingToBuy
    {
        static void Postfix(IEnumerable<Thing> __result)
        {
            Log.Error("POSTFIX WILLING TO BUY START");
            Map map = Current.Game.VisibleMap;
            if (map != null)
            {
                Log.Error("Map found");
                List<Thing> l = new List<Thing>(__result);
                foreach (Building b in map.listerBuildings.allBuildingsColonist)
                {
                    Building_Dresser d = b as Building_Dresser;
                    if (d != null)
                    {
                        Log.Error("Dresser found " + d.Count);
                        l.AddRange(d.Apparel as List<Thing>);
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Notify_ApparelRemoved")]
    static class Patch_Pawn_ApparelTracker_Notify_ApparelRemoved
    {
        static void Postfix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            if (!Main.IsSwapping)
            {
                StoredApparelContainer.Notify_ApparelRemoved(__instance.pawn, apparel);
            }
        }
    }

    [HarmonyPatch(typeof(Settlement_TraderTracker), "RegenerateStock")]
    static class Patch_Settlement_TraderTracker_RegenerateStock
    {
        static void Postfix(Settlement_TraderTracker __instance)
        {
            ThingOwner<Thing> l = (ThingOwner<Thing>)typeof(Settlement_TraderTracker).GetField("stock", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
            foreach (Thing t in Current.Game.VisibleMap.spawnedThings)
            {
                if (t is Building_Dresser)
                {
                    foreach (Thing apparel in ((Building_Dresser)t).StoredApparel)
                    {
                        l.TryAdd(apparel, false);
                    }
                }
            }
        }
    }*/
}
