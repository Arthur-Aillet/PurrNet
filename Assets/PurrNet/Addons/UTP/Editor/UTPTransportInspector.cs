using PurrNet.Editor;
using PurrNet.Transports;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PurrNet.UTP.Editor
{
    [CustomEditor(typeof(UnityTransport), true)]
    public class UTPTransportInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
#if !UTP_TRANSPORT
            EditorGUILayout.HelpBox("UnityTransport is not installed.\nPlease install to use this transport.",
                MessageType.Warning);
            if(GUILayout.Button("Add UnityTransport to Package Manager")) {
                Client.Add("com.unity.transport");
                Client.Resolve();
            }
            return;
#endif

            var transport = (UnityTransport)target;

            base.OnInspectorGUI();
            GUILayout.Space(10);
#if !UTP_RELAY
            if (transport.peerToPeer)
            {
                EditorGUILayout.HelpBox("Unity P2P is not installed.\nPlease install to use this transport with P2P.",
                    MessageType.Warning);
                if (GUILayout.Button("Add Unity P2P to Package Manager"))
                {
                    Client.Add("com.unity.services.multiplayer");
                    Client.Resolve();
                }
            }
#endif
            TransportInspector.DrawTransportStatus(transport);
        }

        private void OnEnable()
        {
            var generic = (UnityTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState += OnDirty;
        }

        private void OnDisable()
        {
            var generic = (UnityTransport)target;
            if (generic && generic.transform != null)
                generic.transport.onConnectionState -= OnDirty;
        }

        private void OnDirty(ConnectionState state, bool asServer)
        {
            Repaint();
        }
    }
}
