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
        private const int DEFAULT_TOTAL_TIMEOUT = -1;
        private int[] kerning = new int[] { 24, 14, 23, 21, 23, 20, 21, 22, 23, 21 };

        private FieldInfo activeColorField;
        private FieldInfo battleUIField;
        private FieldInfo battleUISkinField;
        private FieldInfo commField;
        private FieldInfo leftColorField;
        private FieldInfo roundTimeField;
        private FieldInfo roundTimerField;
        private FieldInfo showClockField;
        private MethodInfo endTurnMethod;
        private MethodInfo showEndTurnMethod;

        private int p1TotalSeconds;
        private int p1TurnSeconds;
        private int p2TotalSeconds;
        private int p2TurnSeconds;
        private int totalTimeout = DEFAULT_TOTAL_TIMEOUT;
        private int timeout = DEFAULT_TIMEOUT;
        private bool turnEnded = false;
        private TileColor activePlayer = TileColor.unknown;

        public TimerChange() {
            activeColorField = typeof(BattleMode).GetField("activeColor", BindingFlags.Instance | BindingFlags.NonPublic);
            battleUIField = typeof(BattleMode).GetField("battleUI", BindingFlags.Instance | BindingFlags.NonPublic);
            battleUISkinField = typeof(BattleMode).GetField("battleUISkin", BindingFlags.Instance | BindingFlags.NonPublic);
            commField = typeof(BattleMode).GetField("comm", BindingFlags.Instance | BindingFlags.NonPublic);
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
            return 4;
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

        private void printTotalTimer(BattleMode target, string text, Rect rect) {
            GUI.skin.label.fontSize = GUI.skin.label.fontSize;
            GUI.color = Color.white;

            for (int i = 0; i < text.Length; i++) {
                if (text[i] != ' ') {
                    int num7 = (int)(text[i] - '0');
                    rect.width = rect.height * (float)kerning[num7] / 34f;
                    if (text.Length == 1) {
                        rect.x += rect.width / 2f;
                    }
                    GUI.DrawTexture(rect, ResourceManager.LoadTexture("BattleMode/Clock/time__n_" + text[i]));
                }
                rect.x += rect.width * 1.1f;
            }
        }

        public override void AfterInvoke(InvocationInfo info, ref object returnValue) {
            // set timeout to user-defined value on game start
            if (info.target is BattleMode && info.targetMethod.Equals("_handleMessage") && timeout < DEFAULT_TIMEOUT) {
                BattleMode target = (BattleMode)info.target;
                Message msg = (Message) info.arguments[0];
                if (msg is GameInfoMessage) {
                    showClockField.SetValue(target, true);
                    roundTimeField.SetValue(target, timeout);
                }
            }
            if (info.target is BattleMode && info.targetMethod.Equals("OnGUI") && (timeout < DEFAULT_TIMEOUT || totalTimeout != DEFAULT_TOTAL_TIMEOUT)) {
                BattleMode target = (BattleMode)info.target;
                TileColor nowActivePlayer = (TileColor)activeColorField.GetValue(target);
                bool playerChanged = nowActivePlayer != activePlayer;
                activePlayer = nowActivePlayer;
                float roundTimer = (float)roundTimerField.GetValue(target);
                float roundTime = (float)roundTimeField.GetValue(target);
                float timePassed = (roundTimer >= 0f) ? Mathf.Floor(Time.time - roundTimer) : 0f;
                if (activeColorField.GetValue(target).Equals(leftColorField.GetValue(target))) {
                    int seconds = Mathf.Max(0, (int)(roundTime + 1 - timePassed)); // add +1 so round stops 1 second AFTER hitting 0
                    p1TurnSeconds = (int)Mathf.Min(timePassed, roundTime);
                    // add last turn time to the total time
                    if (playerChanged) {
                        p2TotalSeconds += p2TurnSeconds;
                        p2TurnSeconds = 0;
                    }
                    if (seconds == 0 && !turnEnded) {
                        turnEnded = true;
                        BattleModeUI battleUI = (BattleModeUI)battleUIField.GetValue(target);
                        showEndTurnMethod.Invoke(battleUI, new object[] { false } );
                        endTurnMethod.Invoke(target, new object[] { });
                    }

                    // out of total time, surrender game!
                    if (totalTimeout != DEFAULT_TOTAL_TIMEOUT && totalTimeout - p1TotalSeconds - p1TurnSeconds < 0) {
                        ((Communicator)commField.GetValue(target)).sendBattleRequest(new SurrenderMessage());
                    }
                }
                else {
                    // add last turn time to the total time
                    if (playerChanged) {
                        p1TotalSeconds += p1TurnSeconds;
                        p1TurnSeconds = 0;
                    }

                    p2TurnSeconds = (int)Mathf.Min(timePassed, roundTime);
                    turnEnded = false;
                }

                // draw indicators for the amount of time left
                if (totalTimeout != DEFAULT_TOTAL_TIMEOUT) {
                    GUISkin skin = GUI.skin;
                    GUI.skin = (UnityEngine.GUISkin) battleUISkinField.GetValue(target);
                    // light transparency on the text background
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
                    GUI.skin = skin;

                    int elapsedTimeP1 = totalTimeout - p1TotalSeconds - p1TurnSeconds;
                    int elapsedMinsP1 = elapsedTimeP1 / 60;
                    int elapsedSecsP1 = elapsedTimeP1 % 60;
                    string p1Text = elapsedMinsP1 + " " + elapsedSecsP1;
                    Rect p1Rect = new Rect((float)(Screen.width / 2) - width / 2f - namesBoxX + Screen.height * 0.025f, (float)Screen.height * 0.035f, 0f, (float)Screen.height * 0.03f);
                    printTotalTimer(target, p1Text, p1Rect);

                    int elapsedTimeP2 = totalTimeout - p2TotalSeconds - p2TurnSeconds;
                    int elapsedMinsP2 = elapsedTimeP2 / 60;
                    int elapsedSecsP2 = elapsedTimeP2 % 60;
                    string p2Text = elapsedMinsP2 + " " + elapsedSecsP2;
                    Rect p2Rect = new Rect((float)(Screen.width / 2) - width / 2f + namesBoxX + Screen.height * 0.025f, (float)Screen.height * 0.035f, 0f, (float)Screen.height * 0.03f);
                    printTotalTimer(target, p2Text, p2Rect);
                }
            }
        }

        public void onReconnect() {
            // don't care
            return;
        }

        private int splitMinutesAndSeconds(string text, out int seconds) {
            string[] split = text.Split(new char[] {':','.',','});
            if (split.Length == 2) {
                seconds = Convert.ToInt32(split[1]);
                return Convert.ToInt32(split[0]);
            }
            else {
                seconds = Convert.ToInt32(split[0]);
                return 0;
            }
        }

        public void handleMessage(Message msg) {
            if (msg is RoomChatMessageMessage) {
                RoomChatMessageMessage rcMsg = (RoomChatMessageMessage)msg;
                if (isTimerChangeMsg(rcMsg)) {
                    bool emitError = false;
                    string[] cmds = rcMsg.text.Split(' ');
                    RoomChatMessageMessage newMsg = new RoomChatMessageMessage();
                    newMsg.from = GetName(); // name of the mod, that is
                    newMsg.roomName = App.ArenaChat.ChatRooms.GetCurrentRoom();
                    switch (cmds.Length) {
                    case 2:
                        newMsg.roomName = App.ArenaChat.ChatRooms.GetCurrentRoom();
                        try {
                            timeout = Convert.ToInt32(cmds[1]);
                            if (timeout > 0 && timeout < DEFAULT_TIMEOUT) {
                                totalTimeout = DEFAULT_TOTAL_TIMEOUT;
                                newMsg.text = "Turn timeout set to " + timeout + " seconds. Total timeout disabled.";
                            }
                            else {
                                emitError = true;
                            }
                        }
                        catch (Exception) {
                            emitError = true;
                        }
                        break;
                    case 3:
                        newMsg.roomName = App.ArenaChat.ChatRooms.GetCurrentRoom();
                        try {
                            timeout = Convert.ToInt32(cmds[1]);
                            int seconds;
                            int minutes = splitMinutesAndSeconds(cmds[2], out seconds);
                            totalTimeout = minutes * 60 + seconds;
                            if (timeout > 0 && timeout < DEFAULT_TIMEOUT && totalTimeout > 0) {
                                newMsg.text = "Turn timeout set to " + timeout + " seconds. Total timeout set to " + minutes + " minutes and " + seconds + " seconds.";
                            }
                            else {
                                emitError = true;
                            }
                        }
                        catch (Exception) {
                            emitError = true;
                        }
                        break;
                    } 
                    if (emitError) {
                        timeout = DEFAULT_TIMEOUT;
                        totalTimeout = DEFAULT_TOTAL_TIMEOUT;
                        newMsg.text = "Invalid command. Turn timeout set to default. Total timeout disabled.";
                    }
                    App.ChatUI.handleMessage(newMsg);
                    App.ArenaChat.ChatRooms.ChatMessage(newMsg);
                }
            }
        }
        
        private bool isTimerChangeMsg(RoomChatMessageMessage msg) {
            string[] cmds = msg.text.ToLower().Split(' ');
            if (msg.from == App.MyProfile.ProfileInfo.name) {
                return cmds[0].Equals("/timerchange") || cmds[0].Equals("/tc");
            }
            return false;
        }
    }
}
