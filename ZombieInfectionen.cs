using System;
using System.Collections.Generic;
using System.Linq;
using GTA;
using GTA.Native;
using GTA.Math;

public class ZombieInfection : Script
{
    private List<Ped> zombies = new List<Ped>();
    private List<Ped> infectedAnimals = new List<Ped>();
    private List<Ped> survivors = new List<Ped>();
    private List<Blip> targetBlips = new List<Blip>();
    private Random random = new Random();
    private bool bloodApplied = false;
    private Vector3 indoorAreaCenter = new Vector3(0, 0, 0); // Beispielkoordinaten für Innenbereich
    private float indoorAreaRadius = 50f; // Radius des Innenbereichs
    private bool hasInfectedPedsOrAnimals = false;
    private bool blackoutActive = false; // Status des Blackouts
    private int tickCounter = 0;
    private const int maxPedsPerTick = 10; // Maximale Anzahl der zu verarbeitenden Peds pro Tick
    private int infectedCount = 0; // Zählt die Anzahl der infizierten Peds

    public ZombieInfection()
    {
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Interval = 1000; // Tick-Intervall in Millisekunden
        AddBloodEffect(Game.Player.Character);
        bloodApplied = true;
    }

    private void OnKeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
    {
        if (e.KeyCode == System.Windows.Forms.Keys.O)
        {
            ToggleBlackout();
        }
        else if (e.KeyCode == System.Windows.Forms.Keys.X)
        {
            InfectNearestPed();
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        tickCounter++;

        // Verteilt die Last auf mehrere Ticks
        if (tickCounter % 5 == 0) UpdateBlips();
        if (tickCounter % 2 == 0) UpdateZombies();
        if (tickCounter % 4 == 0) UpdateInfectedAnimals();
        if (tickCounter % 5 == 0) UpdateGangsCopsMilitaryAndAltruists();
        if (tickCounter % 6 == 0) UpdateSurvivors();

        // Überprüfe, ob der Spielercharakter oder die Kleidung gewechselt wurde
        Ped player = Game.Player.Character;
        AddBloodEffect(player); // Bluttextur bei jedem Tick anwenden

        if (!bloodApplied || player.IsInVehicle() || player.IsReloading || player.IsGettingUp || player.IsSwimming || player.IsWalking || player.IsRunning)
        {
            AddBloodEffect(player);
            bloodApplied = true;
        }

        // Erstelle gelegentlich Überlebende, aber nur wenn bereits Peds oder Tiere infiziert wurden
        if (random.NextDouble() < 0.10) // Erhöhte Wahrscheinlichkeit (10%) bei jedem Tick
        {
            CreateRandomSurvivor();
        }

        // Überprüfe, ob genügend Peds infiziert wurden, um Überlebende zu erzeugen
        CheckAndCreateSurvivors();
    }

    private void ToggleBlackout()
    {
        blackoutActive = !blackoutActive;
        Function.Call(Hash._SET_BLACKOUT, blackoutActive);
        UI.Notify(blackoutActive ? "Blackout aktiviert" : "Blackout deaktiviert");
    }

    private void UpdateBlips()
    {
        int processedCount = 0;
        foreach (Ped ped in World.GetAllPeds())
        {
            if (processedCount >= maxPedsPerTick) break; // Verarbeite maximal 10 Peds pro Tick
            if (IsValidTarget(ped) && !HasBlip(ped))
            {
                AddBlip(ped);
                processedCount++;
            }
        }
    }

    private void UpdateZombies()
    {
        int processedCount = 0;
        for (int i = zombies.Count - 1; i >= 0; i--)
        {
            if (processedCount >= maxPedsPerTick) break;
            Ped zombie = zombies[i];
            if (zombie.IsDead)
            {
                zombies.RemoveAt(i);
                continue;
            }

            Ped target = FindClosestNonInfectedPed(zombie.Position, zombie);
            if (target != null && target != zombie)
            {
                if (CanLeaveIndoorArea(zombie))
                {
                    FollowTarget(zombie, target, false);
                }
                else if (!IsInIndoorArea(zombie))
                {
                    FollowTarget(zombie, target, false);
                }

                // Nur Animation setzen, keine Geschwindigkeit mehr hier!
                Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, zombie, "move_m@drunk@verydrunk", 1.0f);
                Function.Call(Hash.SET_PED_CAN_RAGDOLL, zombie, true);
                Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, zombie, 0, false);
                Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, zombie, 46, true);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, zombie, 32, false);
                // Geschwindigkeit wird nur noch in SetPedAttributes gesetzt!

                if (zombie.Position.DistanceTo(target.Position) < 1.5f)
                {
                    if (random.NextDouble() < 0.3)
                    {
                        InfectPed(zombie, target);
                    }
                }
            }
            else
            {
                FindNewTargetForZombie(zombie);
            }
            processedCount++;
        }
    }

    private void UpdateInfectedAnimals()
    {
        int processedCount = 0;
        for (int i = infectedAnimals.Count - 1; i >= 0; i--)
        {
            if (processedCount >= maxPedsPerTick) break;
            Ped animal = infectedAnimals[i];
            if (animal.IsDead)
            {
                infectedAnimals.RemoveAt(i);
                continue;
            }

            // Tiere verfolgen nur Peds, greifen aber keine Peds in Fahrzeugen an
            Ped target = FindClosestNonInfectedPed(animal.Position, animal);
            if (target != null && target != animal && !target.IsInVehicle())
            {
                if (CanLeaveIndoorArea(animal))
                {
                    animal.Task.GoTo(target.Position);
                }
                else if (!IsInIndoorArea(animal))
                {
                    animal.Task.GoTo(target.Position);
                }
            }
            else
            {
                // Finde ein neues Ziel, wenn das aktuelle Ziel ungültig ist
                FindNewTargetForAnimal(animal);
            }
            processedCount++;
        }
    }

    private void UpdateGangsCopsMilitaryAndAltruists()
    {
        int processedCount = 0;
        foreach (Ped ped in World.GetAllPeds())
        {
            if (processedCount >= maxPedsPerTick) break;
            if (ped.IsDead || ped.IsPlayer || zombies.Contains(ped) || infectedAnimals.Contains(ped) || survivors.Contains(ped))
                continue;

            if (IsGangMember(ped) || IsPoliceOfficer(ped) || IsMilitarySoldier(ped) || IsAltruist(ped))
            {
                Ped target = FindClosestPed(ped.Position, ped);
                if (target != null && (zombies.Contains(target) || infectedAnimals.Contains(target)))
                {
                    ped.Task.FightAgainst(target);
                    processedCount++;
                }
            }
        }
    }

    private void UpdateSurvivors()
    {
        int processedCount = 0;
        for (int i = survivors.Count - 1; i >= 0; i--)
        {
            if (processedCount >= maxPedsPerTick) break;
            Ped survivor = survivors[i];
            if (survivor.IsDead)
            {
                survivors.RemoveAt(i);
                continue;
            }

            // Ziel darf kein anderer Survivor sein!
            Ped target = FindClosestEnemy(survivor.Position);
            // Wenn das Ziel tot ist oder ein Survivor ist, suche ein neues Ziel
            if (target == null || target.IsDead || survivors.Contains(target))
            {
                target = null;
                // Suche nach einem neuen gültigen Ziel
                foreach (Ped possibleTarget in World.GetAllPeds())
                {
                    if (!possibleTarget.IsDead && (zombies.Contains(possibleTarget) || infectedAnimals.Contains(possibleTarget)) && !survivors.Contains(possibleTarget))
                    {
                        if (survivor.Position.DistanceTo(possibleTarget.Position) < 10.0f)
                        {
                            target = possibleTarget;
                            break;
                        }
                    }
                }
            }
            if (target != null && !target.IsDead && !survivors.Contains(target) && survivor.Position.DistanceTo(target.Position) < 10.0f)
            {
                survivor.Task.FightAgainst(target);
            }
            processedCount++;
        }
    }

    private bool IsValidTarget(Ped ped)
    {
        return !ped.IsDead && !ped.IsPlayer && !zombies.Contains(ped) && !infectedAnimals.Contains(ped);
    }

    private void InfectNearestPed()
    {
        Ped player = Game.Player.Character;
        Ped target = FindClosestNonInfectedPed(player.Position, player);
        if (target != null)
        {
            InfectPed(player, target);
        }
    }

    private Ped FindClosestNonInfectedPed(Vector3 position, Ped ignorePed)
    {
        Ped closestPed = null;
        float closestDistance = float.MaxValue;

        foreach (Ped ped in World.GetAllPeds())
        {
            if (ped == ignorePed || ped.IsDead || infectedAnimals.Contains(ped) || zombies.Contains(ped) || ped.IsPlayer) continue;

            float distance = position.DistanceTo(ped.Position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPed = ped;
            }
        }

        return closestPed;
    }

    private Ped FindClosestEnemy(Vector3 position)
    {
        Ped closestPed = null;
        float closestDistance = float.MaxValue;

        foreach (Ped ped in World.GetAllPeds())
        {
            if (ped.IsDead) continue;

            bool isEnemy = zombies.Contains(ped) || infectedAnimals.Contains(ped);
            if (!isEnemy) continue;

            float distance = position.DistanceTo(ped.Position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPed = ped;
            }
        }

        return closestPed;
    }

    private Ped FindClosestPed(Vector3 position, Ped ignorePed)
    {
        Ped closestPed = null;
        float closestDistance = float.MaxValue;

        foreach (Ped ped in World.GetAllPeds())
        {
            if (ped == ignorePed || ped.IsDead) continue;

            float distance = position.DistanceTo(ped.Position);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPed = ped;
            }
        }

        return closestPed;
    }

    private void InfectPed(Ped infector, Ped target)
    {
        if (IsAnimal(infector))
        {
            PlayAttackAnimation(target);
            return;
        }
        zombies.Remove(target);
        // Runner-Liste gibt es nicht mehr
        // 20% Chance für schnellen Zombie, sonst langsam
        bool isFastZombie = false;
        if (random.NextDouble() < 0.2)
        {
            isFastZombie = true;
        }
        zombies.Add(target);
        RemoveBlip(target);
        AddBloodEffect(target);
        if (IsStoryCharacter(target))
        {
            InterruptCurrentAction(target);
        }
        SetZombieAttributes(target, isFastZombie);
        if (survivors.Contains(target))
        {
            survivors.Remove(target);
        }
        PedFollowLogic(target, infector, isFastZombie);
        hasInfectedPedsOrAnimals = true;
        infectedCount++;
    }

    private void PedFollowLogic(Ped follower, Ped target, bool isFastZombie)
    {
        Vector3 targetPos = target.Position;
        if (isFastZombie)
        {
            follower.Task.RunTo(targetPos);
        }
        else
        {
            follower.Task.GoTo(targetPos, true);
        }
        Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, follower, targetPos.X, targetPos.Y, targetPos.Z, isFastZombie ? 4.0f : 2.0f, 0, 0, 786603, 0xbf800000);
    }

    private void SetZombieAttributes(Ped target, bool isFastZombie)
    {
        if (isFastZombie)
        {
            // Normale Geh-Animation für schnelle Zombies
            Function.Call(Hash.RESET_PED_MOVEMENT_CLIPSET, target, 0);
            // SCHNELLE ZOMBIES MACHEN
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, target, 2.0f);
            Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, target.Handle, 1.0f);
        }
        else
        {
            // Langsame Geh-Animation für Zombies
            Function.Call(Hash.SET_PED_MOVEMENT_CLIPSET, target, "move_m@drunk@verydrunk", 1.0f);
            // ZOMBIE LANGSAM MACHEN
            Function.Call(Hash.SET_PED_MOVE_RATE_OVERRIDE, target, 0.3f);
            Function.Call(Hash.SET_RUN_SPRINT_MULTIPLIER_FOR_PLAYER, target.Handle, 0.3f);
        }

        target.Weapons.RemoveAll(); // Entferne alle Waffen

        // Set aggressive behavior without specific attack animations
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, target, 46, true); // Always fight
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, target, 2); // Will advance
        Function.Call(Hash.SET_PED_COMBAT_RANGE, target, 2); // Medium range
        Function.Call(Hash.SET_PED_TARGET_LOSS_RESPONSE, target, 1); // Search for targets
        Function.Call(Hash.SET_PED_COMBAT_ABILITY, target, 2); // Professional
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, target, 0, false); // Disable fleeing
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, target, true); // Enable ragdoll
        Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, target, true); // Enable critical hits
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, target, true); // Prevent temporary events
        Function.Call(Hash.SET_PED_CAN_EVASIVE_DIVE, target, false); // Disable evasive diving
        Function.Call(Hash.SET_PED_SHOOT_RATE, target, 1000); // High shoot rate to simulate aggressive behavior
        Function.Call(Hash.SET_PED_SEEING_RANGE, target, 1000f); // Increase seeing range
        Function.Call(Hash.SET_PED_HEARING_RANGE, target, 1000f); // Increase hearing range

        // Set attributes to prevent panicking
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, target, 0, true); // Disable fleeing
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, target, 5, true); // Always fight
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, target, 17, false); // Disable panic
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, target, 46, true); // Disable fleeing when shot
        Function.Call(Hash.SET_PED_CONFIG_FLAG, target, 118, false); // Disable panicking in response to explosions
        Function.Call(Hash.SET_PED_CONFIG_FLAG, target, 208, true); // Disable ragdoll on hit by vehicle
        Function.Call(Hash.SET_PED_CONFIG_FLAG, target, 188, true); // Disable reactions to being shot
        Function.Call(Hash.STOP_PED_SPEAKING, target, true); // Stop ped from speaking

        // Prevent entering vehicles
        Function.Call(Hash.SET_PED_CONFIG_FLAG, target, 32, false); // Prevent entering vehicles
    }

    private void PlayAttackAnimation(Ped animal)
    {
        Function.Call(Hash.TASK_PLAY_ANIM, animal, "creatures@rottweiler@melee@", "dog_takedown_bite", 8.0f, -8.0f, -1, 1, 0, false, false, false);
        Function.Call(Hash.APPLY_DAMAGE_TO_PED, animal, 10, true); // Damage the target to simulate an attack
    }

    private bool IsAnimal(Ped ped)
    {
        PedHash[] animalHashes = {
            PedHash.Boar, PedHash.Cat, PedHash.ChickenHawk, PedHash.Chimp, PedHash.Chop, PedHash.Cormorant, PedHash.Cow,
            PedHash.Coyote, PedHash.Crow, PedHash.Deer, PedHash.Dolphin, PedHash.Fish, PedHash.Hen, PedHash.Husky,
            PedHash.MountainLion, PedHash.Pig, PedHash.Pigeon, PedHash.Poodle, PedHash.Pug, PedHash.Rabbit, PedHash.Rat,
            PedHash.Retriever, PedHash.Rhesus, PedHash.Rottweiler, PedHash.Seagull, PedHash.Shepherd, PedHash.Stingray, PedHash.Westy
        };
        return Array.Exists(animalHashes, element => element == (PedHash)ped.Model.Hash);
    }

    private bool IsGangMember(Ped ped)
    {
        return ped.Model == PedHash.BallaEast01GMY ||
               ped.Model == PedHash.BallaOrig01GMY ||
               ped.Model == PedHash.BallaSout01GMY ||
               ped.Model == PedHash.MexGoon01GMY ||
               ped.Model == PedHash.MexGoon02GMY ||
               ped.Model == PedHash.MexGoon03GMY;
    }

    private bool IsPoliceOfficer(Ped ped)
    {
        return ped.Model == PedHash.Cop01SFY ||
               ped.Model == PedHash.Cop01SMY ||
               ped.Model == PedHash.Sheriff01SFY ||
               ped.Model == PedHash.Sheriff01SMY;
    }

    private bool IsMilitarySoldier(Ped ped)
    {
        return ped.Model == PedHash.Marine01SMY ||
               ped.Model == PedHash.Marine02SMY ||
               ped.Model == PedHash.Marine03SMY;
    }

    private bool IsAltruist(Ped ped)
    {
        return ped.Model == (PedHash)0x4E7DAE1F; // Modell-Hash für Altruist
    }

    private bool IsStoryCharacter(Ped ped)
    {
        return ped.Model == PedHash.AmandaTownley ||
               ped.Model == PedHash.Fabien ||
               ped.Model == PedHash.Floyd ||
               ped.Model == PedHash.JimmyDisanto ||
               ped.Model == PedHash.LamarDavis ||
               ped.Model == PedHash.TracyDisanto;
    }

    private void InterruptCurrentAction(Ped ped)
    {
        ped.Task.ClearAllImmediately();
    }

    private void AddBloodEffect(Ped ped)
    {
        Function.Call(Hash.APPLY_PED_DAMAGE_PACK, ped, "BigHitByVehicle", 0.0f, 1.0f);
        Function.Call(Hash.APPLY_PED_DAMAGE_PACK, ped, "SCR_Torture", 0.0f, 1.0f);
        Function.Call(Hash.APPLY_PED_DAMAGE_PACK, ped, "Explosion_Med", 0.0f, 1.0f);
    }

    private void AddBlip(Ped ped)
    {
        Blip blip = ped.AddBlip();
        blip.Color = BlipColor.Red;
        blip.Scale = 0.8f;
        blip.IsShortRange = true;
        targetBlips.Add(blip);
    }

    private void RemoveBlip(Ped ped)
    {
        Blip blip = ped.CurrentBlip;
        if (blip != null)
        {
            blip.Remove();
            targetBlips.Remove(blip);
        }
    }

    private bool HasBlip(Ped ped)
    {
        return ped.CurrentBlip != null;
    }

    private void FollowTarget(Ped follower, Ped target, bool isFastZombie)
    {
        Vector3 targetPos = target.Position;
        if (isFastZombie)
        {
            follower.Task.RunTo(targetPos);
        }
        else
        {
            follower.Task.GoTo(targetPos, true);
        }
        Function.Call(Hash.TASK_GO_TO_COORD_ANY_MEANS, follower, targetPos.X, targetPos.Y, targetPos.Z, isFastZombie ? 4.0f : 2.0f, 0, 0, 786603, 0xbf800000);
    }

    private void FollowAndAttackTarget(Ped animal, Ped target)
    {
        animal.Task.ClearAll();
        animal.Task.GoTo(target.Position);
        animal.Task.FightAgainst(target);
    }

    private bool IsInIndoorArea(Ped ped)
    {
        return ped.Position.DistanceTo(indoorAreaCenter) <= indoorAreaRadius;
    }

    private bool CanLeaveIndoorArea(Ped ped)
    {
        foreach (Ped otherPed in World.GetAllPeds())
        {
            if (otherPed != ped && IsInIndoorArea(otherPed) && !IsInfected(otherPed))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsInfected(Ped ped)
    {
        return zombies.Contains(ped) || infectedAnimals.Contains(ped);
    }

    private void CreateRandomSurvivor()
    {
        Ped ped = GetRandomPed();
        if (ped != null && IsValidSurvivor(ped))
        {
            if (ped.IsInVehicle())
            {
                Vehicle veh = ped.CurrentVehicle;
                ped.Task.LeaveVehicle();
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 32, false); // Prevent entering vehicles
                Function.Call(Hash.TASK_WANDER_STANDARD, ped, 0, 0);
            }
            survivors.Add(ped);
            GiveRandomWeapon(ped);
            SetSurvivorAttributes(ped);
            // Survivor greift direkt den nächsten Infizierten an
            Ped infectedTarget = FindClosestEnemy(ped.Position);
            if (infectedTarget != null)
            {
                ped.Task.FightAgainst(infectedTarget);
            }
        }
    }

    private Ped GetRandomPed()
    {
        // Nur Peds auswählen, wenn mindestens ein Ped oder Tier infiziert wurde
        if (!hasInfectedPedsOrAnimals) return null;
        List<Ped> allPeds = World.GetAllPeds().Where(p => IsValidSurvivor(p)).ToList();
        if (allPeds.Count > 0)
        {
            return allPeds[random.Next(allPeds.Count)];
        }
        return null;
    }

    private bool IsValidSurvivor(Ped ped)
    {
        return !ped.IsDead && !ped.IsPlayer && !zombies.Contains(ped) && !infectedAnimals.Contains(ped) && !survivors.Contains(ped) && !IsAnimal(ped);
    }

    private void GiveRandomWeapon(Ped ped)
    {
        WeaponHash[] meleeWeapons = {
            WeaponHash.Bat, WeaponHash.Crowbar, WeaponHash.Hammer, WeaponHash.Knife, WeaponHash.Machete, WeaponHash.Wrench
        };

        WeaponHash[] rangedWeapons = {
            WeaponHash.Pistol, WeaponHash.CombatPistol, WeaponHash.MicroSMG, WeaponHash.PumpShotgun, WeaponHash.AssaultRifle
        };

        if (random.NextDouble() < 0.5) // 50% Chance für Nahkampfwaffen
        {
            ped.Weapons.Give(meleeWeapons[random.Next(meleeWeapons.Length)], 1, true, true);
        }
        else // 50% Chance für Schusswaffen
        {
            ped.Weapons.Give(rangedWeapons[random.Next(rangedWeapons.Length)], random.Next(30, 121), true, true);
        }
    }

    private void SetSurvivorAttributes(Ped ped)
    {
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true); // Always fight
        Function.Call(Hash.SET_PED_COMBAT_MOVEMENT, ped, 2); // Will advance
        Function.Call(Hash.SET_PED_COMBAT_RANGE, ped, 2); // Medium range
        Function.Call(Hash.SET_PED_TARGET_LOSS_RESPONSE, ped, 1); // Search for targets
        Function.Call(Hash.SET_PED_COMBAT_ABILITY, ped, 2); // Professional
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, false); // Disable fleeing
        Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped, true); // Enable ragdoll
        Function.Call(Hash.SET_PED_SUFFERS_CRITICAL_HITS, ped, true); // Enable critical hits
        Function.Call(Hash.SET_BLOCKING_OF_NON_TEMPORARY_EVENTS, ped, true); // Prevent temporary events
        Function.Call(Hash.SET_PED_CAN_EVASIVE_DIVE, ped, false); // Disable evasive diving
        Function.Call(Hash.SET_PED_SHOOT_RATE, ped, 1000); // High shoot rate to simulate aggressive behavior
        Function.Call(Hash.SET_PED_SEEING_RANGE, ped, 1000f); // Increase seeing range
        Function.Call(Hash.SET_PED_HEARING_RANGE, ped, 1000f); // Increase hearing range

        // Set attributes to prevent panicking
        Function.Call(Hash.SET_PED_FLEE_ATTRIBUTES, ped, 0, true); // Disable fleeing
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 5, true); // Always fight
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 17, false); // Disable panic
        Function.Call(Hash.SET_PED_COMBAT_ATTRIBUTES, ped, 46, true); // Disable fleeing when shot
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 118, false); // Disable panicking in response to explosions
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 208, true); // Disable ragdoll on hit by vehicle
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 188, true); // Disable reactions to being shot
        Function.Call(Hash.STOP_PED_SPEAKING, ped, true); // Stop ped from speaking

        // Prevent entering vehicles
        Function.Call(Hash.SET_PED_CONFIG_FLAG, ped, 32, false); // Prevent entering vehicles
    }

    private void FindNewTargetForZombie(Ped zombie)
    {
        Ped newTarget = FindClosestNonInfectedPed(zombie.Position, zombie);
        if (newTarget != null && newTarget != zombie)
        {
            FollowTarget(zombie, newTarget, false);
        }
    }

    private void FindNewTargetForAnimal(Ped animal)
    {
        Ped newTarget = FindClosestNonInfectedPed(animal.Position, animal);
        if (newTarget != null && newTarget != animal)
        {
            FollowAndAttackTarget(animal, newTarget);
        }
    }

    private void CheckAndCreateSurvivors()
    {
        // Bedingung, um Überlebende zu erzeugen, wenn genügend Peds infiziert wurden
        if (infectedCount >= 5) // Zum Beispiel, wenn 5 Peds infiziert wurden
        {
            // Überprüfe, ob bereits genügend Überlebende existieren
            if (survivors.Count < 5) // Zum Beispiel, maximal 5 Überlebende
            {
                if (random.NextDouble() < 0.10) // Wahrscheinlichkeit (10%) für die Erstellung eines Überlebenden
                {
                    CreateRandomSurvivor();
                }
                infectedCount = 0; // Zähler zurücksetzen
            }
        }
    }
}
