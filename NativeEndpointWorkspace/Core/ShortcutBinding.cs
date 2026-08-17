using System;
using System.Xml.Serialization;

namespace NativeEndpointWorkspace.Core
{
    [Serializable]
    public class ShortcutBinding
    {
        public int CellId { get; set; }
        public bool Control { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public bool Win { get; set; }
        public int KeyCode { get; set; }

        [NonSerialized]
        private string _status;

        [XmlIgnore]
        public string Status
        {
            get { return _status ?? string.Empty; }
            set { _status = value; }
        }

        public ShortcutBinding Clone()
        {
            return new ShortcutBinding
            {
                CellId = CellId,
                Control = Control,
                Shift = Shift,
                Alt = Alt,
                Win = Win,
                KeyCode = KeyCode,
                Status = Status
            };
        }

        public string GestureText
        {
            get
            {
                string text = string.Empty;
                if (Control) text += "Ctrl+";
                if (Shift) text += "Shift+";
                if (Alt) text += "Alt+";
                if (Win) text += "Win+";
                text += KeyName;
                return text;
            }
        }

        public string KeyName
        {
            get
            {
                if (KeyCode >= 0x70 && KeyCode <= 0x7B)
                    return "F" + (KeyCode - 0x6F);
                return "VK_0x" + KeyCode.ToString("X2");
            }
        }

        public string ConflictKey
        {
            get
            {
                return (Control ? "C" : "-") +
                       (Shift ? "S" : "-") +
                       (Alt ? "A" : "-") +
                       (Win ? "W" : "-") + ":" + KeyCode;
            }
        }
    }
}
