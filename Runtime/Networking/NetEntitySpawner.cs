using System.Collections.Generic;
using AlpineLib.Actors;
using AlpineLib.App;
using AlpineLib.DI;
using AlpineLib.Netcode.Replication;
using AlpineLib.Sessions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AlpineLib.Networking {
    /// <summary>
    /// Turns replicated entities into scene objects: instantiates the registry prefab behind every spawn
    /// the session announces, binds it to its entity, and hands a pawn to the brain that belongs to it —
    /// the local player's controller for the pawn this client owns, a <see cref="NetController"/> for
    /// everybody else's.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Possession is the whole difference between a local and a remote pawn — the prefab is the same,
    /// nothing is stripped, and <see cref="PossessionGate"/> switches on the components that only make
    /// sense on the pawn the player is actually driving. A game that spawns its own player while offline
    /// must stand that path down inside a session, so the session stays the sole source of pawns once
    /// there is one.
    /// </para>
    /// <para>
    /// It belongs on the app root rather than in a game scene because a session outlives every scene it
    /// travels through: entities are announced once, and a scene load would otherwise destroy their
    /// scene objects with nothing left to re-create them. Instead the views are rebuilt from the client
    /// world after every scene load, so a match change re-instantiates the same roster into the new
    /// scene.
    /// </para>
    /// <para>
    /// Games extend it rather than fork it: <see cref="DecorateEntity"/> dresses a fresh instance in
    /// whatever the game knows about the entity, and the three possession hooks re-route who takes the
    /// body. Services are resolved by hand rather than through <see cref="InjectAttribute"/> so the
    /// component is inert — not broken — in a build with no networking configured.
    /// </para>
    /// </remarks>
    public class NetEntitySpawner : MonoBehaviour {
        private readonly Dictionary<uint, NetEntityView> _viewsByEntityId = new Dictionary<uint, NetEntityView>();

        private ISessionService _sessionService;
        private ClientReplication _boundReplication;

        /// <summary>
        /// Adds whatever the game knows about an entity to the object standing in for it — an
        /// appearance, a nameplate, a team colour. Does nothing by default.
        /// </summary>
        /// <remarks>
        /// Called for every kind, after the view is bound and before a pawn is possessed, so an override
        /// may read the binding and may still decide what the brain will find on the object.
        /// </remarks>
        protected virtual void DecorateEntity(GameObject instance, NetEntity entity) { }

        /// <summary>
        /// Picks the brain that drives the pawn this client owns: the first <see cref="Controller"/> on
        /// the actor that steers it through movement intents rather than placing it outright.
        /// </summary>
        /// <remarks>
        /// Externally driven controllers are skipped by their own declaration rather than by type, so a
        /// game's cutscene or spectator brain is excluded on the same terms as
        /// <see cref="NetController"/> without this method knowing either type. A game whose prefab
        /// carries several player brains overrides this rather than reordering its components.
        /// </remarks>
        protected virtual Controller ResolveLocalController(Actor actor) {
            Controller[] controllers = actor.GetComponents<Controller>();

            foreach (Controller controller in controllers) {
                if (controller is NetController) continue;
                if (controller.DrivesPawnExternally) continue;

                return controller;
            }

            Debug.LogError($"NetEntitySpawner::ResolveLocalController->{actor.name} carries no locally driveable Controller.");
            return null;
        }

        /// <summary>
        /// Hands the pawn this client owns to its brain.
        /// </summary>
        /// <remarks>
        /// Routed through <see cref="PlayerService"/> when one is installed, so the camera, the escape
        /// menu and everything else that watches <see cref="IPlayerService.OnPlayerSpawned"/> meets a
        /// networked pawn exactly as it meets an offline one. The service is missing only where none was
        /// composed at all, and there the controller still takes the body so the pawn is at least
        /// driveable.
        /// </remarks>
        protected virtual void PossessAsLocalPlayer(Actor actor) {
            Controller controller = ResolveLocalController(actor);

            if (controller == null) return;

            PlayerService playerService = ResolvePlayerService();

            if (playerService == null) {
                controller.Possess(actor);
                return;
            }

            playerService.SetPlayer(actor, controller);
        }

        /// <summary>Hands somebody else's pawn to the controller that follows their reported pose.</summary>
        protected virtual void PossessAsRemotePlayer(Actor actor) {
            var controller = actor.GetComponent<NetController>();

            if (controller == null) {
                Debug.LogError($"NetEntitySpawner::PossessAsRemotePlayer->{actor.name} carries no NetController.");
                return;
            }

            controller.Possess(actor);
        }

        private void Start() {
            if (!Injector.HasInstance) return;

            if (Injector.Instance.TryResolve(out _sessionService)) return;

            Debug.LogWarning("NetEntitySpawner::Start->No session service; networked spawns are inert.");
        }

        private void OnEnable() {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDisable() {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
        }

        private void OnDestroy() {
            UnbindReplication();
        }

        /// <remarks>
        /// Polled rather than driven by a session event because the client world is created and disposed
        /// with each connection attempt, and the service exposes the live one rather than announcing it.
        /// The cost while offline is one null comparison a frame.
        /// </remarks>
        private void Update() {
            ClientReplication replication = _sessionService?.Replication;

            if (ReferenceEquals(replication, _boundReplication)) return;

            UnbindReplication();

            if (replication == null) return;

            BindReplication(replication);
        }

        private void BindReplication(ClientReplication replication) {
            _boundReplication = replication;
            _boundReplication.OnEntitySpawned += HandleEntitySpawned;
            _boundReplication.OnEntityDespawned += HandleEntityDespawned;
            _boundReplication.OnEntityEvent += HandleEntityEvent;

            RebuildViews();
        }

        /// <summary>
        /// Drops every object this spawner created and stops following the client world it created them
        /// for. Called when a session ends and when this object goes away with it.
        /// </summary>
        private void UnbindReplication() {
            if (_boundReplication == null) return;

            _boundReplication.OnEntitySpawned -= HandleEntitySpawned;
            _boundReplication.OnEntityDespawned -= HandleEntityDespawned;
            _boundReplication.OnEntityEvent -= HandleEntityEvent;
            _boundReplication = null;

            DestroyAllViews();
        }

        /// <summary>
        /// Re-instantiates the whole known roster into the scene that just loaded.
        /// </summary>
        /// <remarks>
        /// A scene load destroys everything this spawner put in the previous one, and the server does not
        /// re-announce entities it has already announced — a match change is a phase and a scene, not a
        /// new session. Rebuilding from the client world's own list is what keeps the two in step without
        /// any extra traffic.
        /// </remarks>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode) {
            if (_boundReplication == null) return;
            if (mode == LoadSceneMode.Additive) return;

            RebuildViews();
        }

        private void RebuildViews() {
            _viewsByEntityId.Clear();

            foreach (NetEntity entity in _boundReplication.Entities) {
                SpawnEntityView(entity);
            }
        }

        private void HandleEntitySpawned(NetEntity entity) {
            SpawnEntityView(entity);
        }

        private void HandleEntityDespawned(uint entityId) {
            if (!_viewsByEntityId.TryGetValue(entityId, out NetEntityView view)) return;

            _viewsByEntityId.Remove(entityId);

            if (view == null) return;

            view.Unbind();
            Destroy(view.gameObject);
        }

        /// <remarks>
        /// Routed to the remote pawn's controller alone. The owner predicted its own jump on the frame it
        /// was pressed, and the client world already filters the echo of an event this client raised, so
        /// an owned pawn has nothing left to play.
        /// </remarks>
        private void HandleEntityEvent(uint entityId, byte eventId, byte argument) {
            if (!_viewsByEntityId.TryGetValue(entityId, out NetEntityView view)) return;
            if (view == null || view.IsOwned) return;

            var controller = view.GetComponent<NetController>();

            if (controller == null) return;

            controller.PlayEntityEvent(eventId, argument);
        }

        /// <summary>
        /// Instantiates one entity's prefab, binds it and — for a pawn — possesses it. Replaces anything
        /// already standing in for that entity, so a rebuild after a scene load cannot leave two.
        /// </summary>
        /// <remarks>
        /// Everything past the decoration is pawn business. A mover is a moving platform: nobody drives
        /// it, it has no actor to possess and no <see cref="NetController"/> to interpolate it, because
        /// its prefab positions itself from the same scene-authored path both simulations evaluate.
        /// Handing one to <see cref="PossessPawn"/> would log two errors a spawn and, worse, put a
        /// controller on it that fights the path for the transform.
        /// </remarks>
        private void SpawnEntityView(NetEntity entity) {
            if (entity == null) return;

            GameObject prefab = ResolvePrefab(entity.PrefabId);

            if (prefab == null) return;

            HandleEntityDespawned(entity.Id);

            bool isOwned = _boundReplication.IsOwned(entity);
            Vector3 position = entity.State.Position.ToUnity();
            Quaternion rotation = Quaternion.Euler(0f, entity.State.YawDegrees, 0f);

            GameObject instance = Instantiate(prefab, position, rotation);
            NetEntityView view = ResolveView(instance);

            view.Bind(entity, isOwned);
            _viewsByEntityId[entity.Id] = view;

            DecorateEntity(instance, entity);

            if (entity.Kind != EntityKind.Pawn) return;

            PossessPawn(instance, isOwned);
        }

        /// <summary>
        /// Hands the pawn to its brain: the local player's controller when this client owns it, the
        /// network controller when somebody else does.
        /// </summary>
        private void PossessPawn(GameObject instance, bool isOwned) {
            var actor = instance.GetComponent<Actor>();

            if (actor == null) {
                Debug.LogError($"NetEntitySpawner::PossessPawn->{instance.name} carries no Actor.");
                return;
            }

            if (isOwned) {
                PossessAsLocalPlayer(actor);
                return;
            }

            PossessAsRemotePlayer(actor);
        }

        /// <summary>
        /// Finds the view component on a freshly instantiated prefab, adding one when the prefab was
        /// authored without it.
        /// </summary>
        /// <remarks>
        /// Added rather than refused because the binding is what every networked component on the object
        /// reads: a prefab missing the component would otherwise spawn something that can never be told
        /// which entity it is, which is harder to diagnose than a warning here.
        /// </remarks>
        private NetEntityView ResolveView(GameObject instance) {
            var view = instance.GetComponent<NetEntityView>();

            if (view != null) return view;

            Debug.LogWarning($"NetEntitySpawner::ResolveView->{instance.name} carries no NetEntityView; adding one.");
            return instance.AddComponent<NetEntityView>();
        }

        private GameObject ResolvePrefab(ushort prefabId) {
            NetPrefabRegistry registry = _sessionService?.Config?.prefabRegistry;

            if (registry != null) return registry.ResolvePrefab(prefabId);

            Debug.LogError("NetEntitySpawner::ResolvePrefab->No prefab registry configured; nothing can spawn.");
            return null;
        }

        /// <remarks>
        /// Resolved through the interface and narrowed to the concrete service because handing a player
        /// over is deliberately not part of <see cref="IPlayerService"/> — only whoever spawns the player
        /// may do it, which offline is the scene and in a session is this object. A game service that
        /// implements the interface without deriving from <see cref="PlayerService"/> is left alone, and
        /// the controller takes the body directly.
        /// </remarks>
        private PlayerService ResolvePlayerService() {
            if (!Injector.HasInstance) return null;
            if (!Injector.Instance.TryResolve(out IPlayerService playerService)) return null;

            return playerService as PlayerService;
        }

        private void DestroyAllViews() {
            foreach (NetEntityView view in _viewsByEntityId.Values) {
                if (view == null) continue;

                view.Unbind();
                Destroy(view.gameObject);
            }

            _viewsByEntityId.Clear();
        }
    }
}
