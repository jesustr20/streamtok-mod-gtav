using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace StreamTok.GtaV.Race
{
    /// <summary>
    /// Cámara de transmisión: siempre mira la carrera, nunca al jugador. Cada pocos segundos cambia de
    /// plano (detrás, de frente, desde arriba, al costado de la pista) y de piloto: pasa más tiempo
    /// con el líder, pero también muestra al 2.º, al 3.º y a los demás. Usa solo natives (sirve en
    /// cualquier versión de SHVDN3).
    /// </summary>
    internal sealed class RaceCamera
    {
        private int _cam = -1;
        private bool _rendering;
        private int _shotEnd;
        private Vehicle _target;
        private bool _follow;                 // cámara que sigue al auto moviéndose a mano cada frame (sin ATTACH)
        private Vector3 _followOffset;
        private Vector3 _lastTargetPos;
        private int _diagFrames, _diagMoved, _diagAt;
        private readonly Action<string> _log;
        private readonly Random _rng;

        public RaceCamera(Random rng, Action<string> log = null)
        {
            _rng = rng;
            _log = log ?? (_ => { });
        }

        /// <summary>
        /// ranked: pilotos con su distancia, el líder primero. Se llama cada frame mientras haya carrera.
        /// </summary>
        public void Update(IList<KeyValuePair<Vehicle, float>> ranked, RaceTrack track)
        {
            int now = Game.GameTime;
            bool targetOk = _target != null && _target.Exists();
            if (targetOk) Diagnose(now);
            if (_cam != -1 && now < _shotEnd && (targetOk || ranked.Count == 0))
            {
                if (_follow && targetOk)
                {
                    // Se coloca la cámara a mano cada frame con la posición actual del auto.
                    Vector3 pos = _target.GetOffsetPosition(_followOffset);
                    Function.Call(Hash.SET_CAM_COORD, _cam, pos.X, pos.Y, pos.Z);
                }
                return;
            }

            NewShot(ranked, track, now);
        }

        /// <summary>Cuenta en cuántos frames cambia de verdad la posición del auto filmado y lo anota en el log cada 10 s.</summary>
        private void Diagnose(int now)
        {
            Vector3 p = _target.Position;
            _diagFrames++;
            if (_target.Speed > 3f && p.DistanceTo(_lastTargetPos) > 0.01f) _diagMoved++;
            _lastTargetPos = p;
            if (now - _diagAt >= 10000)
            {
                if (_diagAt != 0 && _diagFrames > 0)
                {
                    _log($"Cámara: el auto filmado cambió de posición en {_diagMoved * 100 / _diagFrames}% de {_diagFrames} frames (modo {(_follow ? "manual" : "attach/fijo")}).");
                }
                _diagAt = now;
                _diagFrames = 0;
                _diagMoved = 0;
            }
        }

        private void NewShot(IList<KeyValuePair<Vehicle, float>> ranked, RaceTrack track, int now)
        {
            int old = _cam;
            _target = null;
            _follow = false;
            int cam;

            if (ranked.Count == 0)
            {
                // Sin pilotos todavía: vista de la salida desde arriba.
                Vector3 s = track.PositionAt(0f);
                Vector3 back = track.PositionAt(-30f);
                cam = Create(back.X, back.Y, back.Z + 18f);
                Function.Call(Hash.POINT_CAM_AT_COORD, cam, s.X, s.Y, s.Z);
                _shotEnd = now + 1500;
            }
            else
            {
                int idx = PickIndex(ranked.Count);
                _target = ranked[idx].Key;
                float dist = ranked[idx].Value;
                int style = _rng.Next(0, 10);

                if (style < 3)
                {
                    cam = AttachedShot(0f, -8.5f, 3.2f);               // detrás
                }
                else if (style < 5)
                {
                    cam = AttachedShot(0f, 8.5f, 2.0f);                // de frente, mirando hacia atrás
                }
                else if (style < 7)
                {
                    cam = AttachedShot(0f, -6f, 26f);                  // desde arriba, tipo helicóptero
                }
                else if (!TracksideClear(track, dist, out Vector3 sidePos))
                {
                    cam = AttachedShot(0f, -6f, 26f);                  // el costado estaba tapado: desde arriba
                }
                else
                {
                    // Al costado de la pista, más adelante: el auto pasa frente a la cámara.
                    cam = Create(sidePos.X, sidePos.Y, sidePos.Z);
                    Function.Call(Hash.POINT_CAM_AT_ENTITY, cam, _target.Handle, 0f, 0f, 0.8f, true);
                }

                _shotEnd = now + _rng.Next(6000, 9500);
            }

            Function.Call(Hash.SET_CAM_ACTIVE, cam, true);
            if (!_rendering)
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false);
                _rendering = true;
            }
            _cam = cam;

            if (old != -1)
            {
                Function.Call(Hash.SET_CAM_ACTIVE, old, false);
                Function.Call(Hash.DESTROY_CAM, old, false);
            }
        }

        /// <summary>Busca un punto al costado de la pista, más adelante, con vista libre (sin edificios en medio).</summary>
        private bool TracksideClear(RaceTrack track, float dist, out Vector3 pos)
        {
            pos = Vector3.Zero;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                float ahead = 40f + 15f * attempt;
                Vector3 p = track.PositionAt(dist + ahead);
                Vector3 tan = track.TangentAt(dist + ahead);
                Vector3 left = new Vector3(-tan.Y, tan.X, 0f);
                if (left.Length() > 0.001f) left.Normalize();
                float side = _rng.Next(0, 2) == 0 ? -1f : 1f;
                Vector3 cand = p + left * (side * 11f) + new Vector3(0f, 0f, 2.2f);

                // Que la cámara no quede dentro de un muro y vea la pista (de la cámara al punto de la pista).
                Vector3 look = p + new Vector3(0f, 0f, 1f);
                RaycastResult hit = World.Raycast(cand, look, IntersectFlags.Map | IntersectFlags.Objects);
                RaycastResult back = World.Raycast(p + new Vector3(0f, 0f, 2.2f), cand, IntersectFlags.Map | IntersectFlags.Objects);
                if (!hit.DidHit && !back.DidHit)
                {
                    pos = cand;
                    return true;
                }
            }
            return false;
        }

        private int AttachedShot(float x, float y, float z)
        {
            // Si la cámara quedaría dentro de un muro o edificio, se sube a la vista desde arriba.
            if (z < 20f)
            {
                Vector3 camPos = _target.GetOffsetPosition(new Vector3(x, y, z));
                RaycastResult hit = World.Raycast(_target.Position + new Vector3(0f, 0f, 1f), camPos, IntersectFlags.Map | IntersectFlags.Objects);
                if (hit.DidHit)
                {
                    x = 0f; y = -6f; z = 26f;
                }
            }

            // Sin ATTACH: la cámara se mueve a mano cada frame (ver Update).
            Vector3 start = _target.GetOffsetPosition(new Vector3(x, y, z));
            int cam = Create(start.X, start.Y, start.Z);
            _follow = true;
            _followOffset = new Vector3(x, y, z);
            Function.Call(Hash.POINT_CAM_AT_ENTITY, cam, _target.Handle, 0f, 0f, 0.8f, true);
            return cam;
        }

        private static int Create(float x, float y, float z) =>
            Function.Call<int>(Hash.CREATE_CAM_WITH_PARAMS, "DEFAULT_SCRIPTED_CAMERA", x, y, z, 0f, 0f, 0f, 60f, true, 2);

        /// <summary>El líder la mayor parte del tiempo; el resto reparte entre 2.º, 3.º y cualquiera.</summary>
        private int PickIndex(int count)
        {
            double r = _rng.NextDouble();
            int idx = r < 0.55 ? 0 : r < 0.75 ? 1 : r < 0.88 ? 2 : _rng.Next(0, Math.Min(count, 8));
            return Math.Min(idx, count - 1);
        }

        public void Stop()
        {
            if (_cam != -1)
            {
                Function.Call(Hash.SET_CAM_ACTIVE, _cam, false);
                Function.Call(Hash.DESTROY_CAM, _cam, false);
                _cam = -1;
            }
            if (_rendering)
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false);
                _rendering = false;
            }
            _target = null;
        }
    }
}
