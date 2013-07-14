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
        private const int DEFAULT_TIMEOUT = 91;

        private FieldInfo activeColorField;
        private FieldInfo battleUIField;
        private FieldInfo battleUISkinField;
        private FieldInfo leftColorField;
        private FieldInfo roundTimeField;
        private FieldInfo roundTimerField;
        private FieldInfo showClockField;
        private MethodInfo endTurnMethod;
        private MethodInfo showEndTurnMethod;

        private int p1Seconds;
        private int p2Seconds;
        private int timeout = DEFAULT_TIMEOUT;
        private bool turnEnded = false;
        private int[] kerning = new int[]
        {
            24,
            14,
            23,
            21,
            23,
            20,
            21,
            22,
            23,
            21
        };

        public TimerChange() {
            activeColorField = typeof(BattleMode).GetField("activeColor", BindingFlags.Instance | BindingFlags.NonPublic);
            battleUIField = typeof(BattleMode).GetField("battleUI", BindingFlags.Instance | BindingFlags.NonPublic);
            battleUISkinField = typeof(BattleMode).GetField("battleUISkin", BindingFlags.Instance | BindingFlags.NonPublic);
            leftColorField = typeof(BattleMode).GetField("leftColor", BindingFlags.Instance | BindingFlags.NonPublic);
            roundTimeField = typeof(BattleMode).GetField("roundTime", BindingFlags.Instance | BindingFlags.NonPublic);
            roundTimerField = typeof(BattleMode).GetField("roundTimer", BindingFlags.Instance | BindingFlags.NonPublic);
            showClockField = typeof(BattleMode).GetField("showClock", BindingFlags.Instance | BindingFlags.NonPublic);
            endTurnMethod = typeof(BattleMode).GetMethod("endTurn", BindingFlags.Instance | BindingFlags.NonPublic);
            showEndTurnMethod = typeof(BattleModeUI).GetMethod("ShowEndTurn", BindingFlags.Instance | BindingFlags.NonPublic);

            App.Communicator.addListener(this);
        }

        public static string GetName() {
            return "TimerChange";
        }

        public static int GetVersion() {
            return 3;
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
            if (timeout < DEFAULT_TIMEOUT) {
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
                        p1Seconds = seconds;
                        if (seconds == 0 && !turnEnded) {
                            turnEnded = true;
                            BattleModeUI battleUI = (BattleModeUI)battleUIField.GetValue(target);
                            showEndTurnMethod.Invoke(battleUI, new object[] { false } );
                            endTurnMethod.Invoke(target, new object[] { });
                        }
                    }
                    else {
                        float roundTimer = (float)roundTimerField.GetValue(target);
                        float roundTime = (float)roundTimeField.GetValue(target);
                        float timePassed = (roundTimer >= 0f) ? Mathf.Floor(Time.time - roundTimer) : 0f;
                        int seconds = Mathf.Max(0, (int)(roundTime + 1 - timePassed)); // add +1 so round stops 1 second AFTER hitting 0
                        p2Seconds = seconds;
                        turnEnded = false;
                    }
                }
                if (info.target is BattleMode && info.targetMethod.Equals("OnGUI")) {
                    BattleMode target = (BattleMode)info.target;
                    GUISkin skin = GUI.skin;
                    GUI.skin = (UnityEngine.GUISkin) battleUISkinField.GetValue(target);
                    GUI.color = new Color(1f, 1f, 1f, 0.75f);

                    // position of GUI box containing names
                    float namesBoxX = (float)Screen.width * 0.5f - (float)Screen.height * 0.3f;
                    float namesBoxY = (float)Screen.height * 0.027f;
                    float namesBoxW = (float)Screen.height * 0.6f;

                    // from GUIClock.renderHeight
                    float height = (float)Screen.height * 0.08f;
                    float width = height * 164f / 88f;

                    GUI.DrawTexture(new Rect((float)(Screen.width / 2) - width / 2f - namesBoxX, (float)Screen.height * 0.01f, width, height), ResourceManager.LoadTexture("BattleUI/battlegui_timerbox"));
                    GUI.DrawTexture(new Rect((float)(Screen.width / 2) - width / 2f + namesBoxX, (float)Screen.height * 0.01f, width, height), ResourceManager.LoadTexture("BattleUI/battlegui_timerbox"));

                    GUI.skin.label.fontSize = GUI.skin.label.fontSize;
                    GUI.skin = skin;
                    GUI.color = Color.white;

                    string p1Text = p1Seconds.ToString();
                    Rect p1Rect = new Rect((float)(Screen.width / 2) - width / 2f - namesBoxX + Screen.height * 0.01f, (float)Screen.height * 0.035f, 0f, (float)Screen.height * 0.03f);
                    for (int i = 0; i < p1Text.Length; i++) {
                        int num7 = (int)(p1Text[i] - '0');
                        p1Rect.width = p1Rect.height * (float)kerning[num7] / 34f;
                        if (p1Text.Length == 1) {
                            p1Rect.x += p1Rect.width / 2f;
                        }
                        GUI.DrawTexture(p1Rect, ResourceManager.LoadTexture("BattleMode/Clock/time__n_" + p1Text[i]));
                        p1Rect.x += p1Rect.width * 1.1f;
                    }

                    string p2Text = p2Seconds.ToString();
                    Rect p2Rect = new Rect((float)(Screen.width / 2) - width / 2f + namesBoxX + Screen.height * 0.01f, (float)Screen.height * 0.035f, 0f, (float)Screen.height * 0.03f);
                    for (int i = 0; i < p2Text.Length; i++) {
                        int num7 = (int)(p2Text[i] - '0');
                        p2Rect.width = p2Rect.height * (float)kerning[num7] / 34f;
                        if (p2Text.Length == 1) {
                            p2Rect.x += p2Rect.width / 2f;
                        }
                        GUI.DrawTexture(p2Rect, ResourceManager.LoadTexture("BattleMode/Clock/time__n_" + p2Text[i]));
                        p2Rect.x += p2Rect.width * 1.1f;
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
                        if (timeout > 0 && timeout < DEFAULT_TIMEOUT) {
                            newMsg.text = "Turn timeout set to " + timeout + " seconds.";
                        }
                        else {
                            timeout = DEFAULT_TIMEOUT;
                            newMsg.text = "Turn timeout set to default.";
                        }
                    }
                    catch (Exception) {
                        timeout = DEFAULT_TIMEOUT;
                        newMsg.text = "Invalid command. Turn timeout set to default.";
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
