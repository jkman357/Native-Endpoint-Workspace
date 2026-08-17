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

        [XmlIgnore]
        public bool HasModifier
        {
            get { return Control || Shift || Alt || Win; }
        }

        [XmlIgnore]
        public bool HasSupportedModifier
        {
            get { return Control || Shift || Alt; }
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
                if (KeyCode >= WorkspaceConstants.FunctionKeyFirstVirtualKey && KeyCode <= WorkspaceConstants.FunctionKeyLastVirtualKey)
                    return "F" + (KeyCode - WorkspaceConstants.FunctionKeyDisplayOffset);
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
