using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MessengerAppGui
{
    public enum MessageStatus
    {
        Sent,
        Delivered
    }

    public enum CallType
    {
        Audio,
        Video
    }

    public enum CallStatus
    {
        Initiated,
        Accepted,
        Declined,
        Ended
    }

    public class User
    {
        public int Id { get; set; }
        public string Phone { get; set; }
        public string Username { get; set; }
        public bool NotificationsEnabled { get; set; } = true;
        public HashSet<int> MutedChats { get; } = new HashSet<int>();
    }

    public class Chat
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int OwnerId { get; set; }
        public List<int> ParticipantIds { get; } = new List<int>();
        public List<int> MessageIds { get; } = new List<int>();
        public bool Deleted { get; set; }

        public override string ToString()
        {
            return $"[{Id}] {Title}";
        }
    }

    public class Message
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public int SenderId { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
        public bool Edited { get; set; }
        public MessageStatus Status { get; set; }

        public override string ToString()
        {
            var mark = Edited ? " (изменено)" : "";
            return $"{Id}: {Text}{mark}";
        }
    }

    public class CallSession
    {
        public int Id { get; set; }
        public int CallerId { get; set; }
        public int ReceiverId { get; set; }
        public CallType Type { get; set; }
        public CallStatus Status { get; set; }

        public override string ToString()
        {
            return $"{Id}: {CallerId} -> {ReceiverId} [{Type}] {Status}";
        }
    }

    public class Database
    {
        private int _userId = 1;
        private int _chatId = 1;
        private int _messageId = 1;
        private int _callId = 1;

        public List<User> Users { get; } = new List<User>();
        public List<Chat> Chats { get; } = new List<Chat>();
        public List<Message> Messages { get; } = new List<Message>();
        public List<CallSession> Calls { get; } = new List<CallSession>();

        public User AddUser(string phone, string username)
        {
            var user = new User
            {
                Id = _userId++,
                Phone = phone,
                Username = username
            };
            Users.Add(user);
            return user;
        }

        public Chat AddChat(string title, int ownerId, IEnumerable<int> participants)
        {
            var chat = new Chat
            {
                Id = _chatId++,
                Title = title,
                OwnerId = ownerId
            };
            chat.ParticipantIds.Add(ownerId);
            foreach (var p in participants)
            {
                if (!chat.ParticipantIds.Contains(p))
                    chat.ParticipantIds.Add(p);
            }
            Chats.Add(chat);
            return chat;
        }

        public Message AddMessage(int chatId, int senderId, string text)
        {
            var msg = new Message
            {
                Id = _messageId++,
                ChatId = chatId,
                SenderId = senderId,
                Text = text,
                Timestamp = DateTime.UtcNow,
                Edited = false,
                Status = MessageStatus.Sent
            };
            Messages.Add(msg);
            var chat = Chats.FirstOrDefault(c => c.Id == chatId);
            chat?.MessageIds.Add(msg.Id);
            return msg;
        }

        public CallSession AddCall(int callerId, int receiverId, CallType type)
        {
            var call = new CallSession
            {
                Id = _callId++,
                CallerId = callerId,
                ReceiverId = receiverId,
                Type = type,
                Status = CallStatus.Initiated
            };
            Calls.Add(call);
            return call;
        }
    }

    public class ValidationService
    {
        public bool ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            if (username.Length < 3 || username.Length > 32) return false;
            foreach (var ch in username)
            {
                if (!char.IsLetterOrDigit(ch) && ch != '_' && ch != '-')
                    return false;
            }
            return true;
        }

        public bool ValidateMessageText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            if (text.Length > 2000) return false;
            return true;
        }
    }

    public class MessageManager
    {
        private readonly Database _db;
        private readonly ValidationService _validation;

        public MessageManager(Database db, ValidationService validation)
        {
            _db = db;
            _validation = validation;
        }

        public Message CreateMessage(int chatId, string text, int senderId)
        {
            if (!_validation.ValidateMessageText(text)) return null;
            var chat = _db.Chats.FirstOrDefault(c => c.Id == chatId && !c.Deleted);
            if (chat == null) return null;
            if (!chat.ParticipantIds.Contains(senderId)) return null;
            return _db.AddMessage(chatId, senderId, text);
        }

        public bool EditMessage(int messageId, int editorId, string newText)
        {
            var message = _db.Messages.FirstOrDefault(m => m.Id == messageId);
            if (message == null) return false;
            if (message.SenderId != editorId) return false;
            if ((DateTime.UtcNow - message.Timestamp) > TimeSpan.FromHours(48)) return false;
            if (!_validation.ValidateMessageText(newText)) return false;
            message.Text = newText;
            message.Edited = true;
            return true;
        }
    }

    public class ChatManager
    {
        private readonly Database _db;
        private readonly ValidationService _validation;

        public ChatManager(Database db, ValidationService validation)
        {
            _db = db;
            _validation = validation;
        }

        public Chat CreateChat(string title, int ownerId, IEnumerable<int> participants)
        {
            if (string.IsNullOrWhiteSpace(title)) return null;
            var all = participants.Where(p => _db.Users.Any(u => u.Id == p)).ToList();
            return _db.AddChat(title, ownerId, all);
        }

        public bool UpdateChat(int chatId, int userId, string newTitle)
        {
            var chat = _db.Chats.FirstOrDefault(c => c.Id == chatId && !c.Deleted);
            if (chat == null) return false;
            if (chat.OwnerId != userId) return false;
            if (string.IsNullOrWhiteSpace(newTitle)) return false;
            chat.Title = newTitle;
            return true;
        }

        public bool DeleteChat(int chatId, int userId)
        {
            var chat = _db.Chats.FirstOrDefault(c => c.Id == chatId && !c.Deleted);
            if (chat == null) return false;
            if (chat.OwnerId != userId) return false;
            chat.Deleted = true;
            return true;
        }

        public List<Chat> SearchChats(int userId, string query)
        {
            return _db.Chats
                .Where(c => !c.Deleted && c.ParticipantIds.Contains(userId) &&
                            (string.IsNullOrEmpty(query) || c.Title.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
    }

    public class ProfileService
    {
        private readonly Database _db;
        private readonly ValidationService _validation;

        public ProfileService(Database db, ValidationService validation)
        {
            _db = db;
            _validation = validation;
        }

        public User LoadProfile(int userId)
        {
            return _db.Users.FirstOrDefault(u => u.Id == userId);
        }

        public bool UpdateUsername(int userId, string newUsername)
        {
            if (!_validation.ValidateUsername(newUsername)) return false;
            if (_db.Users.Any(u => u.Username.Equals(newUsername, StringComparison.OrdinalIgnoreCase))) return false;
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return false;
            user.Username = newUsername;
            return true;
        }

        public void SetNotifications(int userId, bool enabled)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return;
            user.NotificationsEnabled = enabled;
        }

        public bool ToggleMuteChat(int userId, int chatId)
        {
            var user = _db.Users.FirstOrDefault(u => u.Id == userId);
            var chat = _db.Chats.FirstOrDefault(c => c.Id == chatId && !c.Deleted);
            if (user == null || chat == null) return false;
            if (!chat.ParticipantIds.Contains(userId)) return false;
            if (user.MutedChats.Contains(chatId))
                user.MutedChats.Remove(chatId);
            else
                user.MutedChats.Add(chatId);
            return true;
        }
    }

    public class CallService
    {
        private readonly Database _db;

        public CallService(Database db)
        {
            _db = db;
        }

        public CallSession StartCall(int callerId, int receiverId, CallType type)
        {
            var caller = _db.Users.FirstOrDefault(u => u.Id == callerId);
            var receiver = _db.Users.FirstOrDefault(u => u.Id == receiverId);
            if (caller == null || receiver == null) return null;
            return _db.AddCall(callerId, receiverId, type);
        }

        public bool EndCall(int callId, int userId)
        {
            var call = _db.Calls.FirstOrDefault(c => c.Id == callId);
            if (call == null) return false;
            if (call.CallerId != userId && call.ReceiverId != userId) return false;
            call.Status = CallStatus.Ended;
            return true;
        }
    }

    static class Store
    {
        public static readonly Database Db = new Database();
        public static readonly ValidationService Validation = new ValidationService();
        public static readonly ChatManager ChatManager = new ChatManager(Db, Validation);
        public static readonly MessageManager MessageManager = new MessageManager(Db, Validation);
        public static readonly ProfileService ProfileService = new ProfileService(Db, Validation);
        public static readonly CallService CallService = new CallService(Db);
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new LoginForm());
        }
    }

    public class LoginForm : Form
    {
        private TextBox txtPhone;
        private TextBox txtUsername;
        private Button btnRegister;
        private Button btnLogin;
        private Label lblInfo;

        public LoginForm()
        {
            Text = "Мессенджер – Вход/Регистрация";
            Width = 400;
            Height = 250;
            InitializeControls();
        }

        private void InitializeControls()
        {
            var lblPhone = new Label { Left = 20, Top = 20, Width = 120, Text = "Телефон:" };
            txtPhone = new TextBox { Left = 150, Top = 20, Width = 200 };

            var lblUsername = new Label { Left = 20, Top = 60, Width = 120, Text = "Никнейм (рег.):" };
            txtUsername = new TextBox { Left = 150, Top = 60, Width = 200 };

            btnRegister = new Button { Left = 20, Top = 110, Width = 150, Text = "Зарегистрироваться" };
            btnLogin = new Button { Left = 200, Top = 110, Width = 150, Text = "Войти по телефону" };

            lblInfo = new Label { Left = 20, Top = 150, Width = 330, Height = 40 };

            btnRegister.Click += BtnRegister_Click;
            btnLogin.Click += BtnLogin_Click;

            Controls.Add(lblPhone);
            Controls.Add(txtPhone);
            Controls.Add(lblUsername);
            Controls.Add(txtUsername);
            Controls.Add(btnRegister);
            Controls.Add(btnLogin);
            Controls.Add(lblInfo);
        }

        private void BtnRegister_Click(object sender, EventArgs e)
        {
            var phone = txtPhone.Text.Trim();
            var username = txtUsername.Text.Trim();
            if (string.IsNullOrWhiteSpace(phone))
            {
                lblInfo.Text = "Введите телефон.";
                return;
            }
            if (!Store.Validation.ValidateUsername(username))
            {
                lblInfo.Text = "Неверный формат никнейма.";
                return;
            }
            if (Store.Db.Users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                lblInfo.Text = "Никнейм уже занят.";
                return;
            }
            var user = Store.Db.AddUser(phone, username);
            lblInfo.Text = "Регистрация успешна. ID: " + user.Id;
            OpenMain(user);
        }

        private void BtnLogin_Click(object sender, EventArgs e)
        {
            var phone = txtPhone.Text.Trim();
            var user = Store.Db.Users.FirstOrDefault(u => u.Phone == phone);
            if (user == null)
            {
                lblInfo.Text = "Пользователь не найден.";
                return;
            }
            OpenMain(user);
        }

        private void OpenMain(User user)
        {
            Hide();
            var main = new MainForm(user);
            main.FormClosed += (s, _) => Close();
            main.Show();
        }
    }

    public class MainForm : Form
    {
        private readonly User _currentUser;
        private TabControl tabs;
        private Button btnLogout;

        private TextBox txtProfileUsername;
        private Button btnProfileSave;
        private CheckBox chkProfileNotifications;

        private ListBox lstChats;
        private TextBox txtChatTitle;
        private TextBox txtChatParticipants;
        private Button btnCreateChat;
        private Button btnRenameChat;
        private Button btnDeleteChat;
        private TextBox txtChatSearch;
        private Button btnSearchChat;

        private ComboBox cmbChatForMessages;
        private ListBox lstMessages;
        private TextBox txtMessageText;
        private Button btnSendMessage;
        private TextBox txtEditMessageId;
        private Button btnEditMessage;

        private TextBox txtCallUserId;
        private ComboBox cmbCallType;
        private Button btnStartCall;
        private ListBox lstCalls;

        private TextBox txtMuteChatId;
        private Button btnToggleMute;

        public MainForm(User user)
        {
            _currentUser = user;
            Text = "Мессенджер – " + _currentUser.Username;
            Width = 800;
            Height = 600;
            InitializeControls();
            LoadChats();
            LoadProfileControls();
        }

        private void InitializeControls()
        {
            tabs = new TabControl { Dock = DockStyle.Fill };

            var tabProfile = new TabPage("Профиль");
            var tabChats = new TabPage("Чаты");
            var tabMessages = new TabPage("Сообщения");
            var tabCalls = new TabPage("Звонки");
            var tabNotifications = new TabPage("Уведомления");

            InitializeProfileTab(tabProfile);
            InitializeChatsTab(tabChats);
            InitializeMessagesTab(tabMessages);
            InitializeCallsTab(tabCalls);
            InitializeNotificationsTab(tabNotifications);

            tabs.TabPages.Add(tabProfile);
            tabs.TabPages.Add(tabChats);
            tabs.TabPages.Add(tabMessages);
            tabs.TabPages.Add(tabCalls);
            tabs.TabPages.Add(tabNotifications);

            btnLogout = new Button { Left = 650, Top = 10, Width = 120, Text = "Выйти" };
            btnLogout.Click += BtnLogout_Click;

            Controls.Add(tabs);
            Controls.Add(btnLogout);
        }

        private void BtnLogout_Click(object sender, EventArgs e)
        {
            Hide();
            var login = new LoginForm();
            login.FormClosed += (s, _) => Close();
            login.Show();
        }

        private void InitializeProfileTab(TabPage tab)
        {
            var lblId = new Label { Left = 20, Top = 20, Width = 200, Text = "ID: " + _currentUser.Id };
            var lblPhone = new Label { Left = 20, Top = 50, Width = 200, Text = "Телефон: " + _currentUser.Phone };

            var lblUsername = new Label { Left = 20, Top = 90, Width = 100, Text = "Никнейм:" };
            txtProfileUsername = new TextBox { Left = 130, Top = 90, Width = 200 };
            chkProfileNotifications = new CheckBox { Left = 20, Top = 130, Width = 250, Text = "Уведомления включены" };
            btnProfileSave = new Button { Left = 20, Top = 170, Width = 150, Text = "Сохранить" };

            btnProfileSave.Click += BtnProfileSave_Click;

            tab.Controls.Add(lblId);
            tab.Controls.Add(lblPhone);
            tab.Controls.Add(lblUsername);
            tab.Controls.Add(txtProfileUsername);
            tab.Controls.Add(chkProfileNotifications);
            tab.Controls.Add(btnProfileSave);
        }

        private void InitializeChatsTab(TabPage tab)
        {
            lstChats = new ListBox { Left = 20, Top = 20, Width = 300, Height = 400 };

            var lblTitle = new Label { Left = 350, Top = 20, Width = 200, Text = "Название чата:" };
            txtChatTitle = new TextBox { Left = 350, Top = 45, Width = 200 };

            var lblParts = new Label { Left = 350, Top = 80, Width = 300, Text = "ID участников через запятую:" };
            txtChatParticipants = new TextBox { Left = 350, Top = 105, Width = 200 };

            btnCreateChat = new Button { Left = 350, Top = 140, Width = 200, Text = "Создать чат" };
            btnRenameChat = new Button { Left = 350, Top = 180, Width = 200, Text = "Переименовать выбранный" };
            btnDeleteChat = new Button { Left = 350, Top = 220, Width = 200, Text = "Удалить выбранный" };

            var lblSearch = new Label { Left = 350, Top = 270, Width = 200, Text = "Поиск по названию:" };
            txtChatSearch = new TextBox { Left = 350, Top = 295, Width = 200 };
            btnSearchChat = new Button { Left = 350, Top = 325, Width = 200, Text = "Найти" };

            btnCreateChat.Click += BtnCreateChat_Click;
            btnRenameChat.Click += BtnRenameChat_Click;
            btnDeleteChat.Click += BtnDeleteChat_Click;
            btnSearchChat.Click += BtnSearchChat_Click;

            tab.Controls.Add(lstChats);
            tab.Controls.Add(lblTitle);
            tab.Controls.Add(txtChatTitle);
            tab.Controls.Add(lblParts);
            tab.Controls.Add(txtChatParticipants);
            tab.Controls.Add(btnCreateChat);
            tab.Controls.Add(btnRenameChat);
            tab.Controls.Add(btnDeleteChat);
            tab.Controls.Add(lblSearch);
            tab.Controls.Add(txtChatSearch);
            tab.Controls.Add(btnSearchChat);
        }

        private void InitializeMessagesTab(TabPage tab)
        {
            var lblChat = new Label { Left = 20, Top = 20, Width = 150, Text = "Чат:" };
            cmbChatForMessages = new ComboBox { Left = 80, Top = 20, Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };

            lstMessages = new ListBox { Left = 20, Top = 60, Width = 500, Height = 350 };

            var lblText = new Label { Left = 20, Top = 430, Width = 100, Text = "Сообщение:" };
            txtMessageText = new TextBox { Left = 120, Top = 430, Width = 300 };
            btnSendMessage = new Button { Left = 430, Top = 430, Width = 90, Text = "Отправить" };

            var lblEditId = new Label { Left = 550, Top = 60, Width = 200, Text = "ID сообщения для ред.: " };
            txtEditMessageId = new TextBox { Left = 550, Top = 85, Width = 100 };
            btnEditMessage = new Button { Left = 550, Top = 115, Width = 150, Text = "Редактировать" };

            cmbChatForMessages.SelectedIndexChanged += CmbChatForMessages_SelectedIndexChanged;
            btnSendMessage.Click += BtnSendMessage_Click;
            btnEditMessage.Click += BtnEditMessage_Click;

            tab.Controls.Add(lblChat);
            tab.Controls.Add(cmbChatForMessages);
            tab.Controls.Add(lstMessages);
            tab.Controls.Add(lblText);
            tab.Controls.Add(txtMessageText);
            tab.Controls.Add(btnSendMessage);
            tab.Controls.Add(lblEditId);
            tab.Controls.Add(txtEditMessageId);
            tab.Controls.Add(btnEditMessage);
        }

        private void InitializeCallsTab(TabPage tab)
        {
            var lblUserId = new Label { Left = 20, Top = 20, Width = 200, Text = "ID пользователя для звонка:" };
            txtCallUserId = new TextBox { Left = 230, Top = 20, Width = 100 };

            var lblType = new Label { Left = 20, Top = 60, Width = 100, Text = "Тип:" };
            cmbCallType = new ComboBox { Left = 130, Top = 60, Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            cmbCallType.Items.Add("Аудио");
            cmbCallType.Items.Add("Видео");
            cmbCallType.SelectedIndex = 0;

            btnStartCall = new Button { Left = 20, Top = 100, Width = 150, Text = "Начать звонок" };
            lstCalls = new ListBox { Left = 20, Top = 150, Width = 400, Height = 250 };

            btnStartCall.Click += BtnStartCall_Click;

            tab.Controls.Add(lblUserId);
            tab.Controls.Add(txtCallUserId);
            tab.Controls.Add(lblType);
            tab.Controls.Add(cmbCallType);
            tab.Controls.Add(btnStartCall);
            tab.Controls.Add(lstCalls);
        }

        private void InitializeNotificationsTab(TabPage tab)
        {
            var lblMute = new Label { Left = 20, Top = 20, Width = 250, Text = "ID чата для mute/unmute:" };
            txtMuteChatId = new TextBox { Left = 20, Top = 45, Width = 100 };
            btnToggleMute = new Button { Left = 140, Top = 43, Width = 150, Text = "Переключить mute" };

            btnToggleMute.Click += BtnToggleMute_Click;

            tab.Controls.Add(lblMute);
            tab.Controls.Add(txtMuteChatId);
            tab.Controls.Add(btnToggleMute);
        }

        private void LoadProfileControls()
        {
            txtProfileUsername.Text = _currentUser.Username;
            chkProfileNotifications.Checked = _currentUser.NotificationsEnabled;
        }

        private void LoadChats(string query = "")
        {
            lstChats.Items.Clear();
            cmbChatForMessages.Items.Clear();
            var chats = Store.ChatManager.SearchChats(_currentUser.Id, query);
            foreach (var c in chats)
            {
                lstChats.Items.Add(c);
                cmbChatForMessages.Items.Add(c);
            }
            if (cmbChatForMessages.Items.Count > 0 && cmbChatForMessages.SelectedIndex == -1)
                cmbChatForMessages.SelectedIndex = 0;
            LoadMessagesForSelectedChat();
        }

        private void LoadMessagesForSelectedChat()
        {
            lstMessages.Items.Clear();
            if (cmbChatForMessages.SelectedItem is Chat chat)
            {
                var messages = Store.Db.Messages
                    .Where(m => m.ChatId == chat.Id)
                    .OrderBy(m => m.Timestamp)
                    .ToList();
                foreach (var m in messages)
                {
                    var sender = Store.Db.Users.FirstOrDefault(u => u.Id == m.SenderId);
                    var mark = m.Edited ? " (изменено)" : "";
                    lstMessages.Items.Add($"[{m.Id}] {sender?.Username}: {m.Text}{mark}");
                }
            }
        }

        private void BtnProfileSave_Click(object sender, EventArgs e)
        {
            var newName = txtProfileUsername.Text.Trim();
            if (!Store.ProfileService.UpdateUsername(_currentUser.Id, newName))
            {
                MessageBox.Show("Не удалось сохранить никнейм.");
                return;
            }
            Store.ProfileService.SetNotifications(_currentUser.Id, chkProfileNotifications.Checked);
            MessageBox.Show("Профиль обновлён.");
            Text = "Мессенджер – " + _currentUser.Username;
        }

        private void BtnCreateChat_Click(object sender, EventArgs e)
        {
            var title = txtChatTitle.Text.Trim();
            var ids = ParseIds(txtChatParticipants.Text);
            var chat = Store.ChatManager.CreateChat(title, _currentUser.Id, ids);
            if (chat == null)
            {
                MessageBox.Show("Не удалось создать чат.");
                return;
            }
            LoadChats();
        }

        private void BtnRenameChat_Click(object sender, EventArgs e)
        {
            if (lstChats.SelectedItem is Chat chat)
            {
                var newTitle = txtChatTitle.Text.Trim();
                if (Store.ChatManager.UpdateChat(chat.Id, _currentUser.Id, newTitle))
                {
                    LoadChats();
                }
                else
                {
                    MessageBox.Show("Не удалось переименовать чат.");
                }
            }
        }

        private void BtnDeleteChat_Click(object sender, EventArgs e)
        {
            if (lstChats.SelectedItem is Chat chat)
            {
                if (Store.ChatManager.DeleteChat(chat.Id, _currentUser.Id))
                {
                    LoadChats();
                }
                else
                {
                    MessageBox.Show("Не удалось удалить чат.");
                }
            }
        }

        private void BtnSearchChat_Click(object sender, EventArgs e)
        {
            LoadChats(txtChatSearch.Text.Trim());
        }

        private void CmbChatForMessages_SelectedIndexChanged(object sender, EventArgs e)
        {
            LoadMessagesForSelectedChat();
        }

        private void BtnSendMessage_Click(object sender, EventArgs e)
        {
            if (cmbChatForMessages.SelectedItem is Chat chat)
            {
                var text = txtMessageText.Text.Trim();
                var msg = Store.MessageManager.CreateMessage(chat.Id, text, _currentUser.Id);
                if (msg == null)
                {
                    MessageBox.Show("Сообщение не отправлено. Проверьте права и текст.");
                    return;
                }
                txtMessageText.Clear();
                LoadMessagesForSelectedChat();
            }
        }

        private void BtnEditMessage_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtEditMessageId.Text.Trim(), out var id))
            {
                MessageBox.Show("Введите корректный ID сообщения.");
                return;
            }

            if (!(cmbChatForMessages.SelectedItem is Chat chat))
            {
                MessageBox.Show("Выберите чат.");
                return;
            }

            var msg = Store.Db.Messages.FirstOrDefault(m => m.Id == id);
            if (msg == null || msg.ChatId != chat.Id)
            {
                MessageBox.Show("Сообщение не принадлежит текущему чату.");
                return;
            }

            var newText = txtMessageText.Text.Trim();
            if (Store.MessageManager.EditMessage(id, _currentUser.Id, newText))
            {
                txtMessageText.Clear();
                LoadMessagesForSelectedChat();
                MessageBox.Show("Сообщение изменено.");
            }
            else
            {
                MessageBox.Show("Не удалось отредактировать сообщение.");
            }
        }

        private void BtnStartCall_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtCallUserId.Text.Trim(), out var receiverId))
            {
                MessageBox.Show("Введите корректный ID пользователя.");
                return;
            }
            var type = cmbCallType.SelectedIndex == 1 ? CallType.Video : CallType.Audio;
            var call = Store.CallService.StartCall(_currentUser.Id, receiverId, type);
            if (call == null)
            {
                MessageBox.Show("Не удалось начать звонок.");
                return;
            }
            lstCalls.Items.Add(call.ToString());
        }

        private void BtnToggleMute_Click(object sender, EventArgs e)
        {
            if (!int.TryParse(txtMuteChatId.Text.Trim(), out var chatId))
            {
                MessageBox.Show("Введите корректный ID чата.");
                return;
            }
            if (Store.ProfileService.ToggleMuteChat(_currentUser.Id, chatId))
            {
                MessageBox.Show("Настройки mute для чата изменены.");
            }
            else
            {
                MessageBox.Show("Не удалось изменить mute для чата.");
            }
        }

        private static List<int> ParseIds(string input)
        {
            var result = new List<int>();
            if (string.IsNullOrWhiteSpace(input)) return result;
            var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (int.TryParse(p.Trim(), out var id))
                    result.Add(id);
            }
            return result;
        }
    }
}

