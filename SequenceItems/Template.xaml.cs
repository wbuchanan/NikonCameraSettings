using System.ComponentModel.Composition;
using System.Windows;

namespace NikonCameraSettings.SequenceItems {

    [Export(typeof(ResourceDictionary))]
    public partial class Template : ResourceDictionary {

        public Template() {
            InitializeComponent();
        }
    }
}