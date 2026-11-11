using System;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace BrazosRobot
{
    /// <summary>
    /// Lee la ESP32 por el puerto serie en un hilo aparte.
    /// Formato por línea (igual que el firmware): d1..d6,gatilloD,i1..i6,gatilloI
    ///   ángulos en grados, gatillo en % presionado, -1 = sin dato. Las líneas con '#' se ignoran.
    /// También acepta 12 datos (sin gatillos) o 6/7 datos (una sola mano).
    /// Requiere "Api Compatibility Level = .NET Framework" (el menú BRAZOSrobot lo configura).
    /// </summary>
    public class EntradaSerie
    {
        public string estado = "Sin conexión";
        public string ultimaLinea = "";
        public bool conectado;
        public int hz;

        readonly object cerrojo = new object();
        readonly float[,] raw = new float[2, 6];
        readonly float[] grip = new float[2];
        readonly bool[,] tieneEje = new bool[2, 6];
        readonly bool[] tieneGrip = new bool[2];
        int lineas;
        float tHz;
        public bool primerosSonDerecha = true;
        public int manoUnica = 0;   // 0 = derecha, 1 = izquierda (cuando llegan 6 o 7 datos)

#if NET_4_6
        System.IO.Ports.SerialPort puerto;
        Thread hilo;
        volatile bool correr;

        public static string[] Puertos()
        {
            try { return System.IO.Ports.SerialPort.GetPortNames(); } catch { return new string[0]; }
        }

        public void Conectar(string nombre, int baudios)
        {
            Desconectar();
            try
            {
                puerto = new System.IO.Ports.SerialPort(nombre, baudios);
                puerto.ReadTimeout = 200;
                puerto.NewLine = "\n";
                puerto.DtrEnable = true;
                puerto.Open();
                correr = true; conectado = true;
                estado = "Conectado a " + nombre;
                hilo = new Thread(Leer) { IsBackground = true };
                hilo.Start();
            }
            catch (Exception e)
            {
                estado = "No se pudo abrir " + nombre + ": " + e.Message;
                conectado = false;
            }
        }

        public void Desconectar()
        {
            correr = false;
            try { if (hilo != null && hilo.IsAlive) hilo.Join(400); } catch { }
            try { if (puerto != null && puerto.IsOpen) puerto.Close(); } catch { }
            puerto = null; hilo = null;
            if (conectado) estado = "Desconectado";
            conectado = false;
        }

        void Leer()
        {
            while (correr)
            {
                try { Procesar(puerto.ReadLine()); }
                catch (TimeoutException) { }
                catch (Exception e) { estado = "Se perdió la conexión: " + e.Message; correr = false; conectado = false; }
            }
        }
#else
        public static string[] Puertos() { return new string[0]; }
        public void Conectar(string nombre, int baudios)
        {
            estado = "El puerto serie necesita Api Compatibility Level = .NET Framework. Usa el menú BRAZOSrobot > Configurar proyecto y vuelve a darle Play.";
        }
        public void Desconectar() { conectado = false; }
#endif

        static readonly char[] SEP = { ',', ';', ' ', '\t' };

        public void Procesar(string linea)
        {
            if (linea == null) return;
            linea = linea.Trim();
            if (linea.Length == 0) return;
            ultimaLinea = linea;
            if (linea[0] == '#') { estado = linea.Substring(1).Trim(); return; }
            var partes = linea.Split(SEP, StringSplitOptions.RemoveEmptyEntries);
            var n = new float[partes.Length];
            for (int i = 0; i < partes.Length; i++)
                if (!float.TryParse(partes[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return;

            lock (cerrojo)
            {
                if (n.Length >= 12)
                {
                    int h = n.Length / 2;
                    int primero = primerosSonDerecha ? 0 : 1;
                    Guardar(primero, n, 0, h);
                    Guardar(1 - primero, n, h, h);
                }
                else if (n.Length >= 6) Guardar(manoUnica, n, 0, n.Length);
                else return;
                lineas++;
            }
        }

        void Guardar(int mano, float[] n, int desde, int cuantos)
        {
            for (int i = 0; i < 6; i++)
            {
                float v = n[desde + i];
                if (v >= 0) { raw[mano, i] = v; tieneEje[mano, i] = true; }
            }
            if (cuantos >= 7 && n[desde + 6] >= 0) { grip[mano] = Mathf.Clamp(n[desde + 6], 0, 100); tieneGrip[mano] = true; }
        }

        /// Copia lo último que llegó (llamar desde el hilo principal)
        public void Tomar(float[,] r, bool[,] tr, float[] g, bool[] tg)
        {
            lock (cerrojo)
            {
                Array.Copy(raw, r, raw.Length);
                Array.Copy(tieneEje, tr, tieneEje.Length);
                Array.Copy(grip, g, 2);
                Array.Copy(tieneGrip, tg, 2);
            }
        }

        public void Limpiar()
        {
            lock (cerrojo) { Array.Clear(tieneEje, 0, tieneEje.Length); Array.Clear(tieneGrip, 0, 2); }
        }

        public void ContarHz(float dt)
        {
            tHz += dt;
            if (tHz >= 1f) { lock (cerrojo) { hz = lineas; lineas = 0; } tHz = 0; }
        }
    }
}
