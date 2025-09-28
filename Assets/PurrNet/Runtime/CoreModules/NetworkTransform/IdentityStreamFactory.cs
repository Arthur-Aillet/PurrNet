namespace PurrNet.Modules
{
    public class IdentityStreamFactory : SceneScopedFactory<IdentityStreamModule>
    {
        readonly NetworkManager _manager;
        readonly HierarchyFactory _factory;
        readonly DeltaModule _deltaModule;
        readonly ScenePlayersModule _scenePlayers;

        public IdentityStreamFactory(NetworkManager manager, ScenesModule scenes, ScenePlayersModule scenePlayers, HierarchyFactory factory, DeltaModule deltaModule) : base(scenes)
        {
            _manager = manager;
            _factory = factory;
            _deltaModule = deltaModule;
            _scenePlayers = scenePlayers;
        }

        protected override IdentityStreamModule CreateModule(SceneID scene, bool asServer)
        {
            if (_factory.TryGetHierarchy(scene, out var hierarchy))
                return new IdentityStreamModule(_manager, hierarchy, _deltaModule, _scenePlayers, scene);
            throw new System.Exception("Hierarchy not found");
        }
    }
}
