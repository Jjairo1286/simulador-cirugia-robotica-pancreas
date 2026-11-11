// Mando bimanual: 12 encoders AS5600 (dos TCA9548A) + 2 grippers con joystick
// ESP32. Envía una línea por lectura con 14 datos:
//   d1,d2,d3,d4,d5,d6,gatilloD,i1,i2,i3,i4,i5,i6,gatilloI
//
// Ejes en grados (0.00 a 359.91), -1 si el encoder no responde.
// Solo el eje 2 de cada mando lleva filtro: mediana de 5 lecturas + suavizado.
//
// Gatillo en % PRESIONADO (formato que espera el visualizador HTML):
//   0   = joystick suelto   -> pinza ABIERTA (60°)
//   100 = joystick a fondo  -> pinza CERRADA (0°)
//   En la página, "El gatillo llega como" debe decir "0 a 100 % presionado".
//
// Conexiones ESP32:
//   SDA -> GPIO 21, SCL -> GPIO 22
//   TCA derecha:   0x70   |   TCA izquierda: 0x71 (A0 a 3.3 V)
//   Joystick derecho   (VRx o VRy) -> GPIO 34
//   Joystick izquierdo (VRx o VRy) -> GPIO 35
//
// IMPORTANTE:
//   Alimenta los joysticks con 3.3 V. No conectes 5 V a las entradas analógicas.
//   Los pull-ups del bus I2C deben ir a 3.3 V, no a 5 V.
//   No muevas los joysticks durante el primer segundo (calibración del centro).

#include <Wire.h>

// ---------- Pines ESP32 ----------
const int PIN_SDA = 21;
const int PIN_SCL = 22;
const int PIN_JOY_DERECHO   = 34;
const int PIN_JOY_IZQUIERDO = 35;

// ---------- Bus I2C ----------
const uint32_t I2C_HZ = 400000;   // si ves -1 sueltos o datos cruzados, baja a 100000

// ---------- Encoders ----------
const byte AS5600_ADDR   = 0x36;
const byte RAW_ANGLE_REG = 0x0C;
const byte NUM_EJES      = 6;

// ---------- ADC y gatillos ----------
const int   ADC_MAX        = 4095;  // ADC de 12 bits
const int   CENTRO_DEFECTO = 2048;
const float SUAVIZADO      = 0.25;  // suavizado del gatillo (mayor = más rápido)

// Calibración automática del punto muerto
const int   ZONA_MIN       = 60;    // zona muerta mínima (cuentas ADC)
const int   ZONA_MAX       = 400;   // zona muerta máxima
const float ZONA_FACTOR    = 1.5;   // zona = ruido medido en reposo × factor + margen
const int   ZONA_MARGEN    = 40;
const float DERIVA_CENTRO  = 0.002; // qué tan rápido se corrige el centro en reposo (lento)
const bool  INVERTIR_GATILLO = false; // false: suelto = 0 % (pinza ABIERTA), accionado = 100 % (CERRADA)

const unsigned long PERIODO_MS = 16; // ~60 Hz

// ---------- Filtro del eje 2 ----------
const byte  FILTRO_MUESTRAS = 5;
const float ALFA_EJE2       = 0.25;  // menor = más suave; mayor = más rápido

struct Mano {
  const char* nombre;
  byte  tca;
  int   pinJoy;
  float centro;    // punto de reposo (se calibra solo y se sigue corrigiendo)
  int   zona;      // zona muerta calculada a partir del ruido
  float tope;      // mayor desvío visto = 100 % presionado (se aprende al usarlo)
  float gatillo;   // % presionado, ya suavizado
};

struct FiltroEje2 {
  float muestras[FILTRO_MUESTRAS];
  byte cantidad;
  byte siguiente;
  float salida;
  bool iniciado;
};

Mano manos[2] = {
  { "derecha",   0x74, PIN_JOY_DERECHO,   CENTRO_DEFECTO, 150, 1000, 0 },
  { "izquierda", 0x70, PIN_JOY_IZQUIERDO, CENTRO_DEFECTO, 150, 1000, 0 },
};

FiltroEje2 filtrosEje2[2] = {};

unsigned long ultimo = 0;

// ---------- Bus y multiplexores ----------
void iniciarBus() {
  Wire.begin(PIN_SDA, PIN_SCL, I2C_HZ);
  Wire.setTimeOut(5);                 // ms: un encoder colgado no congela el lazo
}

bool escribirTCA(byte tca, byte mascara) {
  Wire.beginTransmission(tca);
  Wire.write(mascara);
  return Wire.endTransmission() == 0;
}

// Cierra un multiplexor; reintenta una vez si falla.
// Si un TCA queda abierto, sus AS5600 responden junto con los del otro (datos cruzados).
bool cerrarTCA(byte tca) {
  if (escribirTCA(tca, 0)) return true;
  delayMicroseconds(50);
  return escribirTCA(tca, 0);
}

bool hayDispositivo(byte direccion) {
  Wire.beginTransmission(direccion);
  return Wire.endTransmission() == 0;
}

// ---------- Encoders ----------
float leerAngulo(byte tca, byte canal) {
  if (!escribirTCA(tca, 1 << canal)) return -1;

  Wire.beginTransmission(AS5600_ADDR);
  Wire.write(RAW_ANGLE_REG);
  if (Wire.endTransmission(false) != 0) return -1;

  if (Wire.requestFrom((uint8_t)AS5600_ADDR, (uint8_t)2) != 2) return -1;

  uint8_t alto = Wire.read();
  uint8_t bajo = Wire.read();
  if (alto & 0xF0) return -1;         // los 4 bits altos deben ser 0: si no, lectura corrupta

  uint16_t raw = ((uint16_t)alto << 8) | bajo;
  return raw * 360.0 / 4096.0;
}

// Diferencia angular más corta, entre -180 y +180
float diferenciaAngular(float a, float b) {
  float diferencia = a - b;
  while (diferencia > 180.0)  diferencia -= 360.0;
  while (diferencia < -180.0) diferencia += 360.0;
  return diferencia;
}

float normalizarAngulo(float angulo) {
  while (angulo >= 360.0) angulo -= 360.0;
  while (angulo < 0.0)    angulo += 360.0;
  return angulo;
}

// Mediana circular de 5 lecturas y suavizado adicional
float filtrarEje2(byte mano, float lectura) {
  FiltroEje2 &f = filtrosEje2[mano];

  f.muestras[f.siguiente] = lectura;
  f.siguiente = (f.siguiente + 1) % FILTRO_MUESTRAS;
  if (f.cantidad < FILTRO_MUESTRAS) f.cantidad++;

  // Diferencias respecto a la lectura actual para manejar el cruce 359°/0°
  float diferencias[FILTRO_MUESTRAS];
  for (byte i = 0; i < f.cantidad; i++) {
    diferencias[i] = diferenciaAngular(f.muestras[i], lectura);
  }

  // Ordena para obtener la mediana
  for (byte i = 1; i < f.cantidad; i++) {
    float valor = diferencias[i];
    int j = i - 1;
    while (j >= 0 && diferencias[j] > valor) {
      diferencias[j + 1] = diferencias[j];
      j--;
    }
    diferencias[j + 1] = valor;
  }

  float mediana = normalizarAngulo(lectura + diferencias[f.cantidad / 2]);

  if (!f.iniciado) {
    f.salida = mediana;
    f.iniciado = true;
  } else {
    f.salida = normalizarAngulo(f.salida + ALFA_EJE2 * diferenciaAngular(mediana, f.salida));
  }
  return f.salida;
}

// ---------- Gatillos ----------
int leerADC(int pin, byte n) {
  long suma = 0;
  for (byte i = 0; i < n; i++) suma += analogRead(pin);
  return suma / n;
}

// Calibración automática al arrancar:
//   mide 100 lecturas en reposo (~0.5 s), toma el promedio como centro y usa el ruido
//   (máximo − mínimo) para decidir la zona muerta. Si detecta que el joystick se movió
//   durante la medición, repite (hasta 4 intentos).
void calibrarJoystick(Mano &m) {
  for (byte intento = 0; intento < 4; intento++) {
    long suma = 0;
    int minimo = ADC_MAX, maximo = 0;
    for (int i = 0; i < 100; i++) {
      int v = analogRead(m.pinJoy);
      suma += v;
      if (v < minimo) minimo = v;
      if (v > maximo) maximo = v;
      delay(5);
    }
    int centro = suma / 100;
    int ruido  = maximo - minimo;

    // Joystick quieto y lejos de los extremos: calibración válida
    if (ruido < 500 && centro > 400 && centro < ADC_MAX - 400) {
      m.centro = centro;
      m.zona   = constrain((int)(ruido * ZONA_FACTOR) + ZONA_MARGEN, ZONA_MIN, ZONA_MAX);
      m.tope   = 0.5f * (max(m.centro, (float)(ADC_MAX - m.centro)) - m.zona);
      Serial.print("# Joystick "); Serial.print(m.nombre);
      Serial.print(": centro=");   Serial.print(centro);
      Serial.print(" ruido=");     Serial.print(ruido);
      Serial.print(" zona muerta="); Serial.println(m.zona);
      return;
    }
    delay(200);
  }
  Serial.print("# Joystick "); Serial.print(m.nombre);
  Serial.println(": no estuvo quieto al calibrar, uso valores por defecto (se corrige solo en reposo)");
}

// Devuelve el % presionado (0 = suelto, 100 = a fondo).
// Cuenta el desvío en cualquier sentido, así no importa hacia dónde empuje el joystick.
// Mientras está en reposo, el centro se va corrigiendo despacio (deriva térmica del ADC).
// El 100 % se aprende solo: es el mayor desvío que se ha visto.
float leerGatillo(Mano &m) {
  int lectura = leerADC(m.pinJoy, 4);                     // promedio de 4: menos ruido del ADC
  float error = lectura - m.centro;
  float desvio = fabs(error) - m.zona;

  if (desvio <= 0) {
    m.centro += DERIVA_CENTRO * error;                     // en reposo: seguir la deriva del centro
  } else if (desvio > m.tope) {
    m.tope = desvio;                                       // recorrido mayor que el conocido: aprenderlo
  }

  float pct = 0;
  if (desvio > 0 && m.tope > 1) {
    pct = 100.0f * desvio / m.tope;
    if (pct > 100) pct = 100;
  }
  if (INVERTIR_GATILLO) pct = 100 - pct;

  m.gatillo += SUAVIZADO * (pct - m.gatillo);
  return m.gatillo;
}

// ---------- Una mano completa: 6 ejes + gatillo ----------
// Devuelve cuántos encoders fallaron.
byte enviarMano(byte indiceMano) {
  Mano &m = manos[indiceMano];
  byte fallos = 0;

  cerrarTCA(manos[1 - indiceMano].tca);                  // el otro mux SIEMPRE cerrado

  for (byte canal = 0; canal < NUM_EJES; canal++) {
    float grados = leerAngulo(m.tca, canal);

    if (canal == 1 && grados >= 0) grados = filtrarEje2(indiceMano, grados);   // eje 2

    if (grados < 0) { Serial.print(-1); fallos++; }
    else            Serial.print(grados, 2);
    Serial.print(',');
  }

  cerrarTCA(m.tca);                                       // apagar este mux antes del otro
  Serial.print(leerGatillo(m), 1);
  return fallos;
}

void setup() {
  Serial.begin(115200);

  analogReadResolution(12);
  analogSetPinAttenuation(PIN_JOY_DERECHO,   ADC_11db);
  analogSetPinAttenuation(PIN_JOY_IZQUIERDO, ADC_11db);

  iniciarBus();
  cerrarTCA(manos[0].tca);
  cerrarTCA(manos[1].tca);

  // Las líneas que empiezan con '#' la página las ignora
  Serial.print("# Mando listo: ");
  for (byte k = 0; k < 2; k++) {
    Serial.print(manos[k].nombre);
    Serial.print(hayDispositivo(manos[k].tca) ? " OK  " : " SIN MULTIPLEXOR  ");
  }
  Serial.println();

  // Calibración automática del punto muerto (no toques los joysticks ~1 s)
  calibrarJoystick(manos[0]);
  calibrarJoystick(manos[1]);
}

void loop() {
  unsigned long ahora = millis();
  if (ahora - ultimo < PERIODO_MS) return;
  ultimo = ahora;

  byte fallos = enviarMano(0);   // derecha
  Serial.print(',');
  fallos += enviarMano(1);       // izquierda
  Serial.println();

  // Si no respondió ningún encoder, el bus probablemente se colgó: se reinicia
  if (fallos >= 2 * NUM_EJES) {
    Wire.end();
    delay(2);
    iniciarBus();
    cerrarTCA(manos[0].tca);
    cerrarTCA(manos[1].tca);
  }
}
