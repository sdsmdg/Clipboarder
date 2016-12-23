﻿using Clipboarder;
using Clipboarder.Encryption;
using Clipboarder.Extension;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using static System.Threading.Tasks.Task;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Clipboarder {

    public class MainFormPresenter {
        ClipboardMonitor clipboardMonitor;      // Monitors Clipboard for change in its content

        private IMainDataLayer view;            // MainForm view
        private string databaseName = "contents.db";
        public string password = null;          // Field for temporarily storing password
        
        Image LastClipboardImage = null;        // Hold last text from clipboard
        string LastClipboardText = null;        // Hold last image from clipboard

        // Stores hotkeys registered
        List<Hotkey> textHotkeys = new List<Hotkey>();
        List<Hotkey> imageHotkeys = new List<Hotkey>();

        Keys[] numKeys = {                      // Array for number keys on top row of keyboard
            Keys.D1, Keys.D2, Keys.D3,
            Keys.D4, Keys.D5, Keys.D6,
            Keys.D7, Keys.D8, Keys.D9
        };

        public MainFormPresenter(IMainDataLayer view) {
            this.view = view;
            Initialize();
            RegisterShortcuts();
        }

        private void Initialize() {
            // Adds handler to view events
            view.OnExiting += OnExiting;
            view.LoadContent += LoadContent;
            view.SaveContent += SaveContent;
            view.ShowSettings += ShowSettings;
            view.URLCalled += URLCalled;

            // Adds first content from clipboard
            if (Clipboard.ContainsText()) {
                LastClipboardText = Clipboard.GetText();
                view.AddNewTextRow(getTextContentFromClipboard());
            } else if (Clipboard.ContainsImage()) {
                view.AddNewImageRow(getImageContentFromClipboard());
                Clipboard.GetText();
            }

            view.ProgressVisibility = false;

            // Adds handler for clipboardContentChanged Event
            clipboardMonitor = new ClipboardMonitor();
            clipboardMonitor.ClipboardChanged += ClipboardMonitor_ClipboardChanged;
        }

        private void URLCalled(object sender, EventArgs e) {
            if (view.SelectedRowText != null) {
                UrlListDisplay urlListDisplay = new UrlListDisplay(this, ContentIdentifier.GetURLs(view.SelectedRowText));
                urlListDisplay.ShowDialog();
            }
        }

        private void OnExiting(object sender, EventArgs e) {
            clipboardMonitor.Dispose();
        }

        /// <summary>
        /// Monitors clipboard content change and notifies views accordingly
        /// </summary>
        private void ClipboardMonitor_ClipboardChanged(object sender, ClipboardChangedEventArgs e) {
            if (Clipboard.ContainsText() && Clipboard.GetText() != LastClipboardText) {
                view.AddNewTextRow(getTextContentFromClipboard());
            } else if (Clipboard.ContainsImage()) {
                view.AddNewImageRow(getImageContentFromClipboard());
            }
        }

        /// <summary>
        /// Returns text content from clipboard as TextContent object
        /// </summary>
        private TextContent getTextContentFromClipboard() {
            TextContent contentToReturn = new TextContent();

            contentToReturn.index = view.TextRowCount + 1;
            contentToReturn.text = Clipboard.GetText();
            contentToReturn.time = System.DateTime.Now.ToShortTimeString();

            return contentToReturn;
        }

        /// <summary>
        /// Returns image content from clipboard as ImageContent object.
        /// </summary>
        private ImageContent getImageContentFromClipboard() {
            ImageContent contentToReturn = new ImageContent();

            contentToReturn.index = view.ImageRowCount + 1;
            contentToReturn.image = Clipboard.GetImage();
            contentToReturn.time = System.DateTime.Now.ToShortTimeString();

            return contentToReturn;
        }

        private void LoadContent(object sender, EventArgs e) {
            ImportAndDisplayEntries();
        }

        private void SaveContent(object sender, EventArgs e) {
            ExportEntries();
        }

        private void ShowSettings(object sender, EventArgs e) {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.ShowDialog();
            UnregisterShortcuts(true);
        }

        #region  Import and Export
        /// <summary>
        /// Import and populates CLipboarder grid with entries from database
        /// using DatabaseOperations, DatabaseReadWrite and User class
        /// </summary>
        private void ImportAndDisplayEntries() {
            if (!File.Exists(System.IO.Path.Combine(Application.StartupPath, "contents.db"))) {
                MessageBox.Show("No content to load." + "\n\n" + "Use Menu > Save Content to save entries.",
                    "Clipboarder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            } else {
                // Creates DatabaseOperations object to perform operations on specified database
                DatabaseOperations dbOperations = new DatabaseOperations();
                
                // Connects to database and opens connection
                try {
                    dbOperations = new DatabaseOperations();
                    dbOperations.ConnectDatabase(databaseName);
                    dbOperations.OpenConnection();
                } catch (Exception ex) {
                    MessageBox.Show("Error connecting database, database does not exists or is unreachable." 
                        + ex.Message + "\n\nOperation aborted.\n" , "Clipboarder Error", 
                        MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    view.status = "Error";
                    dbOperations.CloseConnection();
                    return;
                }

                User user = new User(dbOperations);
                user.GetCurrentUserID();

                // Shows AskPasswordDecrypt form to ask user for password,
                // this call also passes hashed password from the database 
                // to inform user for incorrect password.
                AskPasswordDecrypt askPassword = new AskPasswordDecrypt(this, user.GetUserPassword());
                DialogResult result = askPassword.ShowDialog();

                // If DialogResult.OK == true then AskPasswordDialog will provide password to
                // password field in MainFormPresenter instance earlier passed in constructor
                if (result != DialogResult.OK) {
                    dbOperations.CloseConnection();
                    return;
                }

                // Checks equality of password provided and stored
                if (!user.CurrentUserHasID()) {
                    MessageBox.Show("Content for current user doesn't exists.  \n\nOperation aborted.\n",
                        "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    dbOperations.CloseConnection();
                    return;
                } else {
                    view.ClearAll();
                    DatabaseReadWrite dbContent = new DatabaseReadWrite(dbOperations, user);
                    try {
                        view.status = "Reading text from database";
                        List<string[]> outputList = dbContent.GetTextData();
                        
                        for (int i = 0; i < outputList.Count; i++) {
                            string[] outputString = outputList[i];
                            
                            TextContent contentToAdd = new TextContent();
                            contentToAdd.index = Convert.ToInt32(outputString[0]);
                            contentToAdd.text = StringCipher.Decrypt(outputString[1], password);
                            contentToAdd.time = StringCipher.Decrypt(outputString[2], password);

                            view.AddNewTextRow(contentToAdd);
                        }
                    } catch (Exception) {
                        MessageBox.Show("Error filling table with values. \n  \n\nOperation aborted.\n",
                            "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        view.status = "Error";
                        dbOperations.CloseConnection();
                        return;
                    }

                    try {
                        view.status = "Reading images from database.";

                        List<string[]> outputList = dbContent.GetImageData();
                        
                        //progress bar values
                        view.TaskProgress = 0;
                        view.ProgressVisibility = true;

                        for (int i = 0; i < outputList.Count; i++) {
                            view.TaskProgress = (100 / outputList.Count) * i;
                            string[] outputString = outputList[i];
                            
                            // Decrypt content and add to contentToAdd
                            ImageContent contentToAdd = new ImageContent();
                            contentToAdd.index = Convert.ToInt32(outputString[0]);
                            contentToAdd.image = ImageConversion.Base64ToImage(StringCipher.Decrypt(outputString[1], password));
                            contentToAdd.time = StringCipher.Decrypt(outputString[2], password);

                            view.AddNewImageRow(contentToAdd);
                        }
                        view.TaskProgress = 0;
                    } catch (Exception) {
                        MessageBox.Show("Error filling table with values. \n  \n\nOperation aborted.\n",
                            "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        view.status = "Error";
                        dbOperations.CloseConnection();
                        return;
                    }
                }
                view.status = "Imported successfully";
                view.TaskProgress = 0;
                view.ProgressVisibility = false;
                dbOperations.CloseConnection();
            } // Else statement for check on file existence
            password = "";
        }

        /// <summary>
        /// Exports all clipboarder entries to database
        /// </summary>
        public void ExportEntries() {
            List<TextContent> textContents = view.GetAllTextContent();
            List<ImageContent> imageContents = view.GetAllImageContent();

            if (textContents.Count == 0 && imageContents.Count == 0) {
                MessageBox.Show("No entries to save.", "Clipboarder", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            } else {
                AskPasswordEncrypt askPassword = new AskPasswordEncrypt(this);
                DialogResult result = askPassword.ShowDialog();

                // Uses DatabaseOperations class object to connect and write to database Clipboarder.Extension
                DatabaseOperations dbOperations = new DatabaseOperations();

                if (result == DialogResult.OK) {
                    if (!File.Exists(System.IO.Path.Combine(Application.StartupPath, databaseName))) {
                        //Creates new Database
                        DatabaseOperations.CreatesNewDatabase(databaseName);
                        Properties.Settings.Default.doesDatabaseExists = true;
                    }

                    // Connects to database and opens connection
                    try {
                        dbOperations = new DatabaseOperations();
                        dbOperations.ConnectDatabase(databaseName);
                        dbOperations.OpenConnection();
                    } catch (Exception ex) {
                        MessageBox.Show("Error connecting database. \n\nOperation aborted.\n" + ex.ToString(),
                            "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        dbOperations.CloseConnection();
                        return;
                    }
                    
                    User user = new User(dbOperations);
                    if (user.CurrentUserHasID())
                        user.GetCurrentUserID();
                    DatabaseReadWrite dbContents = new DatabaseReadWrite(dbOperations, user);
                    // Deletes existing content for current user if corresponding record exists in userTable

                    if (user.CurrentUserHasID()) {
                        try {
                            //dbContents.clearAllContent();
                            dbContents.DeleteAllRecordsForCurrentUsers();
                            user.DeleteEntry();
                            user.CreateEntry(BCrypt.HashPassword(password, BCrypt.GenerateSalt(10)));
                            user.GetCurrentUserID();
                        } catch (Exception ex) {
                           DialogResult messageResult = MessageBox.Show("Error deleting existing records from database.\n"
                               + ex.ToString() + "\n\nDo you want to continue?.", "Clipboarder Error",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Stop);
                            dbOperations.CloseConnection();
                            if (messageResult == DialogResult.No) return;
                        }
                    } else {
                        // Hashes password using BCrypt Class and adds new record to userName table in database
                        try {
                            
                            user.CreateEntry(BCrypt.HashPassword(password, BCrypt.GenerateSalt(10)));
                            user.GetCurrentUserID();
                        } catch (Exception ex) {
                            MessageBox.Show("Error adding current user record to database." + ex.ToString() 
                                + "\n\nOperation aborted.\n", "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                            dbOperations.CloseConnection();
                            return;
                        }
                    }

                    //  Adds Content to databased as it is encrypted using password provided by the user
                    
                    // Exports text Entries
                    if (textContents.Count != 0) {
                        view.ProgressVisibility = true;
                        view.TaskProgress = 0;

                        for (int i = 0; i < textContents.Count; i++) {
                            view.TaskProgress = (100 / textContents.Count) * (i + 1);
                            TextContent contentToExport = new TextContent();

                            contentToExport.index = textContents[i].index;
                            contentToExport.text = StringCipher.Encrypt(textContents[i].text, password);
                            contentToExport.time = StringCipher.Encrypt(textContents[i].time, password);
                            
                            // Writing data to database
                            try {
                                dbContents.SetTextContent(contentToExport.index, contentToExport.text, contentToExport.time);
                            } catch (Exception ex) {
                                MessageBox.Show("Error adding content to database. \n\nOperation aborted.\n" 
                                    + ex.ToString(), "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                                dbOperations.CloseConnection();
                                return;
                            }
                        }
                        view.TaskProgress = 0;
                        view.ProgressVisibility = false;
                    }

                    // Exports image Entries
                    if (imageContents.Count != 0) {
                        view.ProgressVisibility = true;
                        view.TaskProgress = 0;

                        for (int i = 0; i < imageContents.Count; i++) {
                            view.TaskProgress = (100 / imageContents.Count) * (i + 1);

                            //Using TextContent since image will be converted Base64 String
                            TextContent contentToAdd = new TextContent();
                            contentToAdd.index = imageContents[i].index;
                            contentToAdd.text = StringCipher.Encrypt(ImageConversion.ImageToBase64(imageContents[i].image, ImageFormat.Png), password);
                            contentToAdd.time = StringCipher.Encrypt(imageContents[i].time, password);
                            
                            // Writing data to database
                            try {
                                dbContents.SetImageContent(contentToAdd.index, contentToAdd.text, contentToAdd.time);
                            } catch (Exception ex) {
                                MessageBox.Show("Error adding content to database. \n\nOperation aborted.\n" 
                                    + ex.ToString(), "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                                dbOperations.CloseConnection();
                                return;
                            }
                        }
                        view.TaskProgress = 0;
                        view.ProgressVisibility = false;
                    }
                    view.status = "Export Completed";
                }
                dbOperations.CloseConnection();
                password = null;
            }
        }

        #endregion

        #region HotKey Bindings
        /// <summary>
        /// Registers hotkeys using HotKey class
        /// </summary>
        private void RegisterShortcuts() {
            //Registers keyboard shortcuts for text content
            if (Properties.Settings.Default.areTextShortcutsEnabled) {
                int keyboardTextShortcuts = Properties.Settings.Default.textShortcuts;

                for (int i = 0; i < keyboardTextShortcuts; i++) {
                    Hotkey newHotkey = new Hotkey();

                    // Gets keys from settings
                    string[] splitted = Properties.Settings.Default.textContentKeys.Split('+');

                    // Chooses Ctrl, Shift and Alt keys according to user preference
                    foreach (string value in splitted) {
                        switch (value) {
                            case "Ctrl":
                                newHotkey.Control = true;
                                break;
                            case "Alt":
                                newHotkey.Alt = true;
                                break;
                            case "Shift":
                                newHotkey.Shift = true;
                                break;
                        }
                    }

                    newHotkey.KeyCode = numKeys[i];

                    // Modifies behaviour for above key combination
                    newHotkey.Pressed += delegate { onTextHotkeyPress(newHotkey); };

                    try {
                        newHotkey.Register((MainForm)view);
                    } catch (Exception ex) {
                        MessageBox.Show("Error registering hotkeys. \n\nOperation aborted.\n\nError:" + ex.ToString(), "Clipboarder Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                        return;
                    }
                    textHotkeys.Add(newHotkey);

                }
            }
        }

        public void UnregisterShortcuts(bool reregisterKeys) {
            for (int i = 0; i < textHotkeys.Count; i++) {
                textHotkeys[i].Unregister();
            }
            textHotkeys.Clear();
            if (reregisterKeys) RegisterShortcuts();
        }

        private void onTextHotkeyPress(Hotkey key) {
            if (Array.IndexOf(numKeys, key.KeyCode) < view.TextRowCount) {
                string temp = null;

                if (Clipboard.ContainsText())
                    temp = Clipboard.GetText();

                LastClipboardText = view.GetTextContentAt(view.TextRowCount - Array.IndexOf(numKeys, key.KeyCode) - 1).text;
                Clipboard.SetText(LastClipboardText);
                SendKeys.SendWait("^v");

                if (temp != null) {
                    LastClipboardText = temp;
                    Clipboard.SetText(LastClipboardText);
                }
            }
        }
        #endregion
    }
}
