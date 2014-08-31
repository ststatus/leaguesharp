﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

using LeagueSharp;
using LeagueSharp.Common;
using SharpDX;

namespace KarthusSharp
{
    class PlayerInfo
    {
        public Obj_AI_Hero Player;
        public int LastSeen;

        public PlayerInfo(Obj_AI_Hero player)
        {
            Player = player;
        }
    }

    class Program
    {
        private static Menu _menu;
        private static IEnumerable<Obj_AI_Hero> _enemyTeam;
        private static Orbwalking.Orbwalker _orbwalker;

        private static int _checkEState;

        private static Spell _spellQ, _spellW, _spellE, _spellR;
        private static SpellSlot _igniteSlot;

        private static List<PlayerInfo> _playerInfo = new List<PlayerInfo>();

        static void Main(string[] args)
        {
            Game.OnGameStart += Game_OnGameStart;

            if (Game.Mode == GameMode.Running)
                Game_OnGameStart(new EventArgs());
        }

        private static void Game_OnGameStart(EventArgs args)
        {
            if (ObjectManager.Player.ChampionName != "Karthus")
                return;

            (_menu = new Menu("KarthusSharp", "KarthusSharp", true)).AddToMainMenu();

            var orbwalkMenu = _menu.AddSubMenu(new Menu("Orbwalking", "Orbwalking"));
            _orbwalker = new Orbwalking.Orbwalker(orbwalkMenu);

            SimpleTs.AddToMenu(_menu.AddSubMenu(new Menu("Target Selector", "TargetSelector")));

            var comboMenu = _menu.AddSubMenu(new Menu("Combo", "Combo"));
            comboMenu.AddItem(new MenuItem("comboKey", "Combo").SetValue(new KeyBind(32, KeyBindType.Press))); //32 == space
            comboMenu.AddItem(new MenuItem("comboQ", "Use Q").SetValue(true));
            comboMenu.AddItem(new MenuItem("comboW", "Use W").SetValue(true));
            comboMenu.AddItem(new MenuItem("comboE", "Use E").SetValue(true));
            comboMenu.AddItem(new MenuItem("comboAA", "Use AA").SetValue(true));

            var harassMenu = _menu.AddSubMenu(new Menu("Harass", "Harass"));
            harassMenu.AddItem(new MenuItem("harassKey", "Harass").SetValue(new KeyBind("X".ToCharArray()[0], KeyBindType.Press)));
            harassMenu.AddItem(new MenuItem("harassQ", "Use Q").SetValue(true));

            var farmMenu = _menu.AddSubMenu(new Menu("Farm", "Farm"));
            farmMenu.AddItem(new MenuItem("lastHitKey", "Last Hit").SetValue(new KeyBind("C".ToCharArray()[0], KeyBindType.Press)));
            farmMenu.AddItem(new MenuItem("laneClearKey", "Lane Clear").SetValue(new KeyBind("V".ToCharArray()[0], KeyBindType.Press)));
            farmMenu.AddItem(new MenuItem("farmQ", "Use Q").SetValue(new StringList(new[] { "Last Hit", "Lane Clear", "Both", "No" }, 1)));
            farmMenu.AddItem(new MenuItem("farmE", "Use E in Lane Clear").SetValue(false));

            var drawMenu = _menu.AddSubMenu(new Menu("Drawing", "Drawing"));
            drawMenu.AddItem(new MenuItem("notifyR", "Alert on R killable enemies").SetValue(true));
            drawMenu.AddItem(new MenuItem("drawQ", "Draw Q range").SetValue(new Circle(true, System.Drawing.Color.FromArgb(125, 0, 255, 0))));

            var miscMenu = _menu.AddSubMenu(new Menu("Misc", "Misc"));
            miscMenu.AddItem(new MenuItem("igniteKS", "Ignite KS").SetValue(true));
            miscMenu.AddItem(new MenuItem("ultKS", "Ultimate KS").SetValue(false));
            miscMenu.AddItem(new MenuItem("packetCast", "Packet Cast (Q,W,R)").SetValue(true));
            miscMenu.AddItem(new MenuItem("debugMode", "Debug (developer only)").SetValue(false).DontSave());

            _spellQ = new Spell(SpellSlot.Q, 875);
            _spellW = new Spell(SpellSlot.W, 1000);
            _spellE = new Spell(SpellSlot.E, 505);
            _spellR = new Spell(SpellSlot.R, 20000f);

            _igniteSlot = ObjectManager.Player.GetSpellSlot("SummonerDot");

            _spellQ.SetSkillshot(1f, 160, 1700, false, SkillshotType.SkillshotCircle);
            _spellW.SetSkillshot(.5f, 80, 1600, false, SkillshotType.SkillshotCircle);
            _spellE.SetSkillshot(1f, 550, 1000, false, SkillshotType.SkillshotCircle);
            _spellR.SetSkillshot(3f, float.MaxValue, float.MaxValue, false, SkillshotType.SkillshotCircle);

            _enemyTeam = ObjectManager.Get<Obj_AI_Hero>().Where(x => x.IsEnemy);

            _playerInfo = _enemyTeam.Select(x => new PlayerInfo(x)).ToList();

            Drawing.OnDraw += Drawing_OnDraw;
            Game.OnGameUpdate += Game_OnGameUpdate;

            Game.PrintChat("<font color=\"#1eff00\">KarthusSharp -</font> <font color=\"#00BFFF\">Loaded" + "</font>");
        }

        static void Game_OnGameUpdate(EventArgs args)
        {
            UpdateLastSeen();

            if (_menu.Item("comboKey").GetValue<KeyBind>().Active)
            {
                bool attack = _menu.Item("comboAA").GetValue<bool>();

                if (!attack && ObjectManager.Player.Mana < 100) //if no mana, allow auto attacks!
                    attack = true;

                _orbwalker.SetAttacks(attack);

                Combo();
            }
            else
            {
                _orbwalker.SetAttacks(true);

                if (_menu.Item("harassKey").GetValue<KeyBind>().Active)
                    Harass();
                else if (_menu.Item("laneClearKey").GetValue<KeyBind>().Active)
                    LaneClear();
                else if (_menu.Item("lastHitKey").GetValue<KeyBind>().Active)
                    LastHit();
                else
                    RegulateEState();
            }

            if (_menu.Item("igniteKS").GetValue<bool>())
                IgniteKS();

            if (_menu.Item("ultKS").GetValue<bool>())
                UltKS();
        }

        static void UltKS()
        {
            if (_spellR.IsReady())
            {
                foreach (Obj_AI_Hero target in _playerInfo.Where(x =>
                    x.Player.IsValid &&
                    !x.Player.IsDead &&
                    x.Player.IsEnemy &&
                    (!x.Player.IsVisible || (x.Player.IsVisible && Utility.IsValidTarget(x.Player))) &&
                    DamageLib.getDmg(x.Player, DamageLib.SpellType.R) >= GetTargetHealth(x, (int)(_spellR.Delay * 1000f))).Select(x => x.Player))
                {
                    if(!_enemyTeam.Any(x => x.IsValid && !x.IsDead && x.IsVisible && ObjectManager.Player.Distance(x) < 1500))
                        _spellR.Cast(ObjectManager.Player.Position, _menu.Item("packetCast").GetValue<bool>());
                }
            }
        }

        static void UpdateLastSeen()
        {
            int time = Environment.TickCount;

            foreach (PlayerInfo playerInfo in _playerInfo.Where(x => x.Player.IsVisible))
                playerInfo.LastSeen = time;
        }

        static void IgniteKS()
        {
            if (_igniteSlot != SpellSlot.Unknown && ObjectManager.Player.SummonerSpellbook.CanUseSpell(_igniteSlot) == SpellState.Ready)
            {
                Obj_AI_Hero target = _enemyTeam.FirstOrDefault(x => Utility.IsValidTarget(x, 600) && DamageLib.getDmg(x, DamageLib.SpellType.IGNITE) >= x.Health);

                if (target != null)
                    ObjectManager.Player.SummonerSpellbook.CastSpell(_igniteSlot, target);
            }
        }

        static void RegulateEState()
        {
            if (ObjectManager.Player.Spellbook.GetSpell(SpellSlot.E).ToggleState == 1)
                _checkEState = 0;
            else if (_checkEState > 0 && _spellE.IsReady()) //otherwise user has manually enabled E
            {
                Obj_AI_Hero target = SimpleTs.GetTarget(_spellE.Range, SimpleTs.DamageType.Magical);

                if (target == null)
                {
                    _spellE.Cast();
                    _checkEState--;
                }
            }
        }

        static void Combo()
        {
            Obj_AI_Hero target;

            if (_menu.Item("comboW").GetValue<bool>() && _spellW.IsReady())
            {
                target = SimpleTs.GetTarget(_spellW.Range, SimpleTs.DamageType.Magical);

                if (target != null)
                    _spellW.Cast(target, _menu.Item("packetCast").GetValue<bool>());
            }

            if (_menu.Item("comboE").GetValue<bool>() && _spellE.IsReady())
            {
                target = SimpleTs.GetTarget(_spellE.Range, SimpleTs.DamageType.Magical);

                if (target != null)
                {
                    if (ObjectManager.Player.Spellbook.GetSpell(SpellSlot.E).ToggleState == 1)
                    {
                        if (ObjectManager.Player.Distance(target.ServerPosition) <= _spellE.Range && _checkEState == 0)
                        {
                            _spellE.Cast();
                            _checkEState++;
                        }
                    }
                }
                else if (ObjectManager.Player.Spellbook.GetSpell(SpellSlot.E).ToggleState == 2 && _checkEState > 0)
                {
                    _spellE.Cast();
                    _checkEState--;
                }
            }

            if (_menu.Item("comboQ").GetValue<bool>() && _spellQ.IsReady())
            {
                target = SimpleTs.GetTarget(_spellQ.Range + _spellQ.Width / 2, SimpleTs.DamageType.Magical);

                if (target != null)
                    _spellQ.CastIfHitchanceEquals(target, HitChance.High, _menu.Item("packetCast").GetValue<bool>());
            }
        }

        static void Harass()
        {
            var target = SimpleTs.GetTarget(_spellQ.Range + _spellQ.Width / 3, SimpleTs.DamageType.Magical);

            if (target != null)
                if (_menu.Item("harassQ").GetValue<bool>() && _spellQ.IsReady())
                    _spellQ.CastIfHitchanceEquals(target, HitChance.High, _menu.Item("packetCast").GetValue<bool>());
        }

        static void LaneClear()
        {
            bool farmQ = _menu.Item("farmQ").GetValue<StringList>().SelectedIndex == 1 || _menu.Item("farmQ").GetValue<StringList>().SelectedIndex == 2;
            bool farmE = _menu.Item("farmE").GetValue<bool>();

            List<Obj_AI_Base> minions;

            MinionManager.FarmLocation farmInfo;

            if (farmQ && _spellQ.IsReady())
            {
                minions = MinionManager.GetMinions(ObjectManager.Player.ServerPosition, _spellQ.Range, MinionTypes.All, MinionTeam.NotAlly, MinionOrderTypes.Health);
                farmInfo = _spellQ.GetCircularFarmLocation(minions, _spellQ.Width);

                if (farmInfo.MinionsHit >= 1)
                    _spellQ.Cast(farmInfo.Position, _menu.Item("packetCast").GetValue<bool>());
            }

            if (farmE && _spellE.IsReady())
            {
                minions = MinionManager.GetMinions(ObjectManager.Player.ServerPosition, _spellE.Range, MinionTypes.All, MinionTeam.NotAlly, MinionOrderTypes.Health);

                bool jungleMobs = minions.Any(x => x.Team == GameObjectTeam.Neutral && x.Health > DamageLib.getDmg(x, DamageLib.SpellType.Q, DamageLib.StageType.FirstDamage)); //FirstDamage = multitarget hit, differentiate! (check radius around mob pos)

                if ((minions.Count >= 3 || jungleMobs) && ObjectManager.Player.Spellbook.GetSpell(SpellSlot.E).ToggleState == 1 && _checkEState == 0)
                {
                    _spellE.Cast();
                    _checkEState++;
                }
                else if ((minions.Count <= 1 && !jungleMobs) && ObjectManager.Player.Spellbook.GetSpell(SpellSlot.E).ToggleState == 2 && _checkEState > 0)
                {
                    _spellE.Cast();
                    _checkEState--;
                }
            }
        }

        static void LastHit()
        {
            bool farmQ = _menu.Item("farmQ").GetValue<StringList>().SelectedIndex == 0 || _menu.Item("farmQ").GetValue<StringList>().SelectedIndex == 2;

            var minions = MinionManager.GetMinions(ObjectManager.Player.ServerPosition, _spellQ.Range, MinionTypes.All, MinionTeam.NotAlly, MinionOrderTypes.Health);

            foreach (var minion in minions.Where(x => DamageLib.getDmg(x, DamageLib.SpellType.Q, DamageLib.StageType.FirstDamage) >= //FirstDamage = multitarget hit, differentiate! (check radius around mob predicted pos)
                HealthPrediction.GetHealthPrediction(x, (int)(_spellQ.Delay * 1000))))
            {
                if (farmQ && _spellQ.IsReady())
                    _spellQ.Cast(minion, _menu.Item("packetCast").GetValue<bool>(), true);
            }
        }

        static void Drawing_OnDraw(EventArgs args)
        {
            if (ObjectManager.Player.IsDead) return;

            var drawQ = _menu.Item("drawQ").GetValue<Circle>();

            if (drawQ.Active)
                Utility.DrawCircle(ObjectManager.Player.Position, _spellQ.Range, drawQ.Color);

            String victims = "";

            int time = Environment.TickCount;

            foreach(Obj_AI_Hero target in _playerInfo.Where(x => 
                x.Player.IsValid && 
                !x.Player.IsDead && 
                x.Player.IsEnemy && 
                ((!x.Player.IsVisible && time - x.LastSeen < 10000) || (x.Player.IsVisible && Utility.IsValidTarget(x.Player))) && 
                DamageLib.getDmg(x.Player, DamageLib.SpellType.R) >= GetTargetHealth(x, (int)(_spellR.Delay * 1000f))).Select(x => x.Player))
            {
                victims += target.ChampionName + " ";
            }

            if (victims != "")
            {
                Drawing.DrawText(Drawing.Width * 0.44f, Drawing.Height * 0.7f, System.Drawing.Color.GreenYellow, "Ult can kill: " + victims);

                //use when pos works
                //new Render.Text((int)(Drawing.Width * 0.44f), (int)(Drawing.Height * 0.7f), "Ult can kill: " + victims, 30, SharpDX.Color.Red); //.Add()
            } 
        }

        public static float GetTargetHealth(PlayerInfo playerInfo, int additionalTime)
        {
            if (playerInfo.Player.IsVisible)
                return playerInfo.Player.Health;

            float predictedhealth = playerInfo.Player.Health + playerInfo.Player.HPRegenRate * ((Environment.TickCount - playerInfo.LastSeen + additionalTime) / 1000f);

            return predictedhealth > playerInfo.Player.MaxHealth ? playerInfo.Player.MaxHealth : predictedhealth;
        }
    }
}