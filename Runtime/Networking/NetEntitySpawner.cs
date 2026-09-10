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
    /// component is inert — not broken — in a build with no networking configured. Every Unity message
    /// here is virtual as well, and an override must call its <c>base</c>: a subclass declaring its own
    /// <c>Update</c> would otherwise hide this one with no compiler diagnostic at all, and the binding
    /// to the client world — which is driven from there — would never happen.
    /// </para>
    /// </remarks>
    public class NetEntitySpawner : MonoBehaviour {
        private readonly Dictionary<uint, NetEntityView> _viewsByEntityId = new Dictionary<uint, NetEntityView>();

        private ISessionService _sessionService;
        private ClientReplication _boundReplication;
        private Actor _announcedPlayer;

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
        /// Externally driven controllers are skipped by their own declaration as well as by type, so a
        /// game's cutscene or spectator brain is excluded on the same terms as
        /// <see cref="NetController"/>, which declares itself external too. A game whose prefab carries
        /// several player brains overrides this rather than reordering its components.
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
        /// Routed through <see cref="IPlayerService"/> when one is installed, so the camera, the escape
        /// menu and everything else that watches <see cref="IPlayerService.OnPlayerSpawned"/> meets a
        /// networked pawn exactly as it meets an offline one. The service is missing only where none was
        /// composed at all, and there the controller still takes the body so the pawn is at least
        /// driveable.
        /// </remarks>
        protected virtual void PossessAsLocalPlayer(Actor actor) {
            Controller controller = ResolveLocalController(actor);

            if (controller == null) return;

            IPlayerService playerService = ResolvePlayerService();

            if (playerService == null) {
                controller.Possess(actor);
                return;
            }

            playerService.SetPlayer(actor, controller);
            _announcedPlayer = actor;
        }

        /// <summary>Hands somebody else's pawn to the controller that follows their reported pose.</summary>
        /// <remarks>
        /// The network controller is the only brain that may hold a remote pawn: it places the body from
        /// the poses its owner reports, so anything else driving it would fight those poses for the
        /// transform. A prefab without one is a composition error rather than a case to fall back from.
        /// </remarks>
        protected virtual void PossessAsRemotePlayer(Actor actor) {
            var controller = actor.GetComponent<NetController>();

            if (controller == null) {
                Debug.LogError($"NetEntitySpawner::PossessAsRemotePlayer->{actor.name} carries no NetController.");
                return;
            }

            controller.Possess(actor);
        }

        /// <remarks>Overrides must call <c>base.Start()</c> or the session is never resolved.</remarks>
        protected virtual void Start() {
            if (!Injector.HasInstance) return;

            if (Injector.Instance.TryResolve(out _sessionService)) return;

            Debug.LogWarning("NetEntitySpawner::Start->No session service; networked spawns are inert.");
        }

        /// <remarks>Overrides must call <c>base.OnEnable()</c> or scene loads stop rebuilding the roster.</remarks>
        protected virtual void OnEnable() {
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        /// <remarks>
        /// Overrides must call <c>base.OnDisable()</c>. Standing the spawner down mid-session is warned
        /// about rather than handled: the session stays bound but stops following scene loads, so the
        /// next one destroys the roster with nothing left to re-create it.
        /// </remarks>
        protected virtual void OnDisable() {
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            if (_boundReplication == null) return;

            Debug.LogWarning("NetEntitySpawner::OnDisable->Disabled while a session is bound; the roster will not survive the next scene load.");
        }

        /// <remarks>Overrides must call <c>base.OnDestroy()</c> or the client world keeps this object's subscriptions.</remarks>
        protected virtual void OnDestroy() {
            UnbindReplication();
        }

        /// <remarks>
        /// Polled rather than driven by a session event because the client world is created and disposed
        /// with each connection attempt, and the service exposes the live one rather than announcing it.
        /// The cost while offline is one null comparison a frame. Overrides must call
        /// <c>base.Update()</c> or nothing ever binds.
        /// </remarks>
        protected virtual void Update() {
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

            ClearAnnouncedPlayer();
            DestroyAllViews();
        }

        /// <summary>
        /// Tells the player service the actor it was handed here is going away.
        /// </summary>
        /// <remarks>
        /// Only on the unbind path: a session ending destroys the pawn the player was driving, and a
        /// service left holding a destroyed actor cannot be told apart from one that never had a player.
        /// A scene-load rebuild deliberately says nothing, because it re-instantiates the same roster and
        /// hands the player straight back in the same frame.
        /// </remarks>
        private void ClearAnnouncedPlayer() {
            // Reference comparison: a pawn destroyed earlier in the session still leaves the service
            // holding it, so the announcement is owed either way.
            if (ReferenceEquals(_announcedPlayer, null)) return;

            _announcedPlayer = null;
            ResolvePlayerService()?.ClearPlayer();
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

        /// <remarks>
        /// The old views are destroyed rather than merely forgotten, because a scene load only accounts
        /// for the instances still sitting in the scene it replaces: one an override moved elsewhere —
        /// under the app root, or through <c>DontDestroyOnLoad</c> — would otherwise survive unreachable
        /// and be duplicated. The roster is copied first because instantiating a prefab and decorating it
        /// runs code this class does not own, and anything of it that reaches back into the session would
        /// mutate the list being walked.
        /// </remarks>
        private void RebuildViews() {
            DestroyAllViews();

            List<NetEntity> roster = new List<NetEntity>(_boundReplication.Entities);

            foreach (NetEntity entity in roster) {
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
        /// Handing one to <see cref="PossessPawn"/> would log an error a spawn and, worse, put a
        /// controller on it that fights the path for the transform.
        /// </remarks>
        private void SpawnEntityView(NetEntity entity) {
            if (entity == null) return;

            GameObject prefab = ResolvePrefab(entity.PrefabId);

            if (prefab == null) return;

            HandleEntityDespawned(entity.Id);

            bool isOwned = _boundReplication.IsOwned(entity);
            GameObject instance = InstantiateAtReportedPose(prefab, entity);
            NetEntityView view = ResolveView(instance);

            view.Bind(entity, isOwned);
            _viewsByEntityId[entity.Id] = view;

            DecorateEntity(instance, entity);

            if (entity.Kind != EntityKind.Pawn) return;

            PossessPawn(instance, isOwned);
        }

        /// <summary>
        /// Instantiates the view where the entity's reported state actually puts it, resolving a carrier
        /// frame before any of its numbers are treated as a place.
        /// </summary>
        /// <remarks>
        /// A pawn riding a carrier reports metres from that carrier's deck. Instantiating those raw drops
        /// a rejoining rider near the world origin and lets its character controller resolve a spawn
        /// against whatever happens to be standing there, a frame before its driver corrects it.
        /// When the carrier is not loaded yet there is no world pose to place it at, so the prefab keeps
        /// its own authored transform and the pawn's own driver places it once the carrier resolves:
        /// <see cref="NetController"/> for a remote pawn, which places it on every sample anyway, and
        /// <see cref="NetActorSync"/> for one this client owns, which nothing else would ever place.
        /// </remarks>
        private GameObject InstantiateAtReportedPose(GameObject prefab, NetEntity entity) {
            PawnState spawnState = entity.State;

            if (!NetCarrierFrame.TryToWorld(in spawnState, out PawnState world)) {
                return Instantiate(prefab, prefab.transform.position, prefab.transform.rotation);
            }

            return Instantiate(prefab, world.Position.ToUnity(), Quaternion.Euler(0f, world.YawDegrees, 0f));
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
        /// Kept at the interface rather than narrowed to <see cref="PlayerService"/>, so a game that
        /// implements <see cref="IPlayerService"/> its own way is handed the player it spawned instead of
        /// being silently skipped. Null means no service is registered at all — the supported offline
        /// composition — or the injector is already gone with the application.
        /// </remarks>
        private IPlayerService ResolvePlayerService() {
            if (!Injector.HasInstance) return null;
            if (!Injector.Instance.TryResolve(out IPlayerService playerService)) return null;

            return playerService;
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
