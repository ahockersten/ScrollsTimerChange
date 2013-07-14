using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

using ScrollsModLoader.Interfaces;
using UnityEngine;
using Mono.Cecil;

namespace TimerChange.mod {
    public class TimerChange : BaseMod, ICommListener {
        private FieldInfo activeColorField;
        private FieldInfo leftColorField;
        private FieldInfo roundTimeField;
        private FieldInfo roundTimerField;
        private FieldInfo showClockField;
        private MethodInfo endTurnMethod;
        private int timeout = 91;

        public TimerChange() {
            activeColorField = typeof(BattleMode).GetField("activeColor", BindingFlags.Instance | BindingFlags.NonPublic);
            leftColorField = typeof(BattleMode).GetField("leftColor", BindingFlags.Instance | BindingFlags.NonPublic);
            roundTimeField = typeof(BattleMode).GetField("roundTime", BindingFlags.Instance | BindingFlags.NonPublic);
            roundTimerField = typeof(BattleMode).GetField("roundTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            showClockField = typeof(BattleMode).GetField("showClock", BindingFlags.Instance | BindingFlags.NonPublic);
            endTurnMethod = typeof(BattleMode).GetMethod("endTurn", BindingFlags.Instance | BindingFlags.NonPublic);

            App.Communicator.addListener(this);
        }

        public static string GetName() {
            return "TimerChange";
        }

        public static int GetVersion() {
            return 1;
        }

        public static MethodDefinition[] GetHooks(TypeDefinitionCollection scrollsTypes, int version) {
            try {
                return new MethodDefinition[] {
                    scrollsTypes["BattleMode"].Methods.GetMethod("_handleMessage", new Type[]{typeof(Message)}),
                    scrollsTypes["BattleMode"].Methods.GetMethod("OnGUI", new Type[]{}),
                };
            }
            catch {
                return new MethodDefinition[] { };
            }
        }

        public override bool BeforeInvoke(InvocationInfo info, out object returnValue) {
            returnValue = null;
            return false;
        }

        public override void AfterInvoke(InvocationInfo info, ref object returnValue) {
            // timeout larger than 90 does not make any sense, smaller than 0 also makes no sense
            if (timeout > 0 && timeout < 90) {
                if (info.target is BattleMode && info.targetMethod.Equals("_handleMessage")) {
                    BattleMode target = (BattleMode)info.target;
                    Message msg = (Message) info.arguments[0];
                    if (msg is GameInfoMessage) {
                        showClockField.SetValue(target, true);
                        roundTimeField.SetValue(target, timeout);
                    }
                }
                if (info.target is BattleMode && info.targetMethod.Equals("OnGUI")) {
                    BattleMode target = (BattleMode)info.target;
                    if (activeColorField.GetValue(target).Equals(leftColorField.GetValue(target))) {
                        float roundTimer = (float)roundTimerField.GetValue(target);
                        float roundTime = (float)roundTimeField.GetValue(target);
                        float timePassed = (roundTimer >= 0f) ? Mathf.Floor(Time.time - roundTimer) : 0f;
                        int seconds = Mathf.Max(0, (int)(roundTime + 1 - timePassed)); // add +1 so round stops 1 second AFTER hitting 0
                        if (seconds == 0) {
                            endTurnMethod.Invoke(target, new object[] {});
                        }
                    }
                }
            }
        }

        public void onReconnect() {
            return;
        }

        public void handleMessage(Message msg) {
            if (msg is RoomChatMessageMessage) {
                RoomChatMessageMessage rcMsg = (RoomChatMessageMessage)msg;
                string[] cmds = rcMsg.text.ToLower().Split(' ');
                if (cmds[0].Equals("/timerchange") || cmds[0].Equals("/tc")) {
                    try {
                        timeout = Convert.ToInt32(cmds[1]);
                        RoomChatMessageMessage newMsg = new RoomChatMessageMessage();
                        newMsg.from = GetName(); // name of the mod, that is
                        newMsg.roomName = App.ArenaChat.ChatRooms.GetCurrentRoom();
                        if (timeout < 0 || timeout > 90) {
                            newMsg.text = "Match timeout set to default.";
                        }
                        else {
                            newMsg.text = "Match timeout set to " + timeout + " seconds";
                        }
                        App.ChatUI.handleMessage(newMsg);
                        App.ArenaChat.ChatRooms.ChatMessage(newMsg);
                    }
                    catch (Exception) {
                    }
                }
            }
        }
    }
}
