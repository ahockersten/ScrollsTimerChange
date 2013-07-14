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
        private const int defaultTimeout = 91;

        private FieldInfo activeColorField;
        private FieldInfo leftColorField;
        private FieldInfo roundTimeField;
        private FieldInfo roundTimerField;
        private FieldInfo showClockField;
        private MethodInfo endTurnMethod;

        private int timeout = defaultTimeout;

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
                    scrollsTypes["ChatRooms"].Methods.GetMethod("ChatMessage", new Type[]{typeof(RoomChatMessageMessage)}),
                };
            }
            catch {
                return new MethodDefinition[] { };
            }
        }

        public override bool BeforeInvoke(InvocationInfo info, out object returnValue) {
            returnValue = null;
            // don't display TimerChange commands in chat
            if (info.target is ChatRooms && info.targetMethod.Equals("ChatMessage")) {
                RoomChatMessageMessage msg = (RoomChatMessageMessage) info.arguments[0];
                if (isTimerChangeMsg(msg)) {
                    return true;
                }
            }
            return false;
        }

        public override void AfterInvoke(InvocationInfo info, ref object returnValue) {
            if (timeout > 0 && timeout < defaultTimeout) {
                // set timeout to user-defined value on game start
                if (info.target is BattleMode && info.targetMethod.Equals("_handleMessage")) {
                    BattleMode target = (BattleMode)info.target;
                    Message msg = (Message) info.arguments[0];
                    if (msg is GameInfoMessage) {
                        showClockField.SetValue(target, true);
                        roundTimeField.SetValue(target, timeout);
                    }
                }
                // end turn if timer has reached 0
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
            // don't care
            return;
        }

        public void handleMessage(Message msg) {
            if (msg is RoomChatMessageMessage) {
                RoomChatMessageMessage rcMsg = (RoomChatMessageMessage)msg;
                if (isTimerChangeMsg(rcMsg)) {
                    string[] cmds = rcMsg.text.ToLower().Split(' ');
                    RoomChatMessageMessage newMsg = new RoomChatMessageMessage();
                    newMsg.from = GetName(); // name of the mod, that is
                    newMsg.roomName = App.ArenaChat.ChatRooms.GetCurrentRoom();
                    try {
                        timeout = Convert.ToInt32(cmds[1]);
                        if (timeout > 0 && timeout < defaultTimeout) {
                            newMsg.text = "Match timeout set to " + timeout + " seconds.";
                        }
                        else {
                            newMsg.text = "Match timeout set to default.";
                        }
                    }
                    catch (Exception) {
                        timeout = defaultTimeout;
                        newMsg.text = "Invalid command. Match timeout set to default.";
                    }
                    App.ChatUI.handleMessage(newMsg);
                    App.ArenaChat.ChatRooms.ChatMessage(newMsg);
                }
            }
        }
        
        private bool isTimerChangeMsg(RoomChatMessageMessage msg) {
            string[] cmds = msg.text.ToLower().Split(' ');
            return cmds[0].Equals("/timerchange") || cmds[0].Equals("/tc");
        }
    }
}
