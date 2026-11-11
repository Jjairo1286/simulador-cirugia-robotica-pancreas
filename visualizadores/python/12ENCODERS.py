import time
import serial
import matplotlib.pyplot as plt


PUERTO = "COM3"       # Cámbialo por el puerto de tu ESP32
BAUDIOS = 115200

NOMBRES = [
    "Derecha 1", "Derecha 2", "Derecha 3", "Derecha 4",
    "Derecha 5", "Derecha 6",
    "Izquierda 1", "Izquierda 2", "Izquierda 3", "Izquierda 4",
    "Izquierda 5", "Izquierda 6",
]


try:
    puerto = serial.Serial(PUERTO, BAUDIOS, timeout=0.02)
    time.sleep(2)
    puerto.reset_input_buffer()
except serial.SerialException as error:
    raise SystemExit(
        f"No pude abrir {PUERTO}. Revisa el puerto COM y cierra el Monitor serie.\n{error}"
    )


fig = plt.figure(figsize=(13, 11))
fig.suptitle("Brújulas de los 12 encoders AS5600", fontsize=16)

rejilla = fig.add_gridspec(4, 4, height_ratios=[1, 1, 1, 0.55])

brujulas = []
agujas = []
textos = []

# Crea 12 brújulas: 6 de la mano derecha y 6 de la izquierda.
for i in range(12):
    fila = i // 4
    columna = i % 4
    ax = fig.add_subplot(rejilla[fila, columna], projection="polar")

    # 0° arriba; los ángulos aumentan en sentido horario.
    ax.set_theta_zero_location("N")
    ax.set_theta_direction(-1)
    ax.set_ylim(0, 1)

    ax.set_xticks([
        0,
        0.5 * 3.14159265,
        3.14159265,
        1.5 * 3.14159265,
    ])
    ax.set_xticklabels(["N", "E", "S", "O"])
    ax.set_yticks([])
    ax.grid(True)
    ax.set_title(NOMBRES[i], pad=12)

    aguja, = ax.plot([0, 0], [0, 0.82], linewidth=3, color="crimson")
    ax.scatter([0], [0], color="black", s=25, zorder=5)
    texto = ax.text(
        0.5, 0.25, "--.-°",
        transform=ax.transAxes,
        ha="center",
        va="center",
        fontsize=11,
        weight="bold",
    )

    brujulas.append(ax)
    agujas.append(aguja)
    textos.append(texto)


# Indicadores de gatillos en la parte inferior.
ax_gatillo_d = fig.add_subplot(rejilla[3, 0:2])
ax_gatillo_i = fig.add_subplot(rejilla[3, 2:4])

for ax, titulo in [
    (ax_gatillo_d, "Gatillo derecho"),
    (ax_gatillo_i, "Gatillo izquierdo"),
]:
    ax.set_title(titulo)
    ax.set_xlim(0, 100)
    ax.set_ylim(0, 1)
    ax.set_yticks([])
    ax.set_xlabel("Porcentaje presionado")
    ax.grid(axis="x")


barra_d = ax_gatillo_d.barh([0.5], [0], height=0.35, color="royalblue")[0]
barra_i = ax_gatillo_i.barh([0.5], [0], height=0.35, color="darkorange")[0]

texto_d = ax_gatillo_d.text(50, 0.5, "0%", ha="center", va="center", color="black")
texto_i = ax_gatillo_i.text(50, 0.5, "0%", ha="center", va="center", color="black")

plt.tight_layout()


def actualizar():
    # El ESP32 envía:
    # D1..D6, gatillo derecho, I1..I6, gatillo izquierdo
    ultima_lectura = None

    while puerto.in_waiting:
        linea = puerto.readline().decode("utf-8", errors="ignore").strip()

        if not linea or linea.startswith("#"):
            continue

        columnas = linea.split(",")
        if len(columnas) != 14:
            continue

        try:
            ultima_lectura = [float(x) for x in columnas]
        except ValueError:
            continue

    if ultima_lectura is None:
        return

    # Encoders derechos: columnas 0 a 5.
    # Encoders izquierdos: columnas 7 a 12.
    indices = list(range(6)) + list(range(7, 13))

    for i, indice in enumerate(indices):
        grados = ultima_lectura[indice]

        if grados < 0:  # -1 significa lectura inválida
            agujas[i].set_data([], [])
            textos[i].set_text("SIN LECTURA")
        else:
            angulo = grados * 3.14159265 / 180.0
            agujas[i].set_data([angulo, angulo], [0, 0.82])
            textos[i].set_text(f"{grados:.1f}°")

    gatillo_d = max(0, min(100, ultima_lectura[6]))
    gatillo_i = max(0, min(100, ultima_lectura[13]))

    barra_d.set_width(gatillo_d)
    barra_i.set_width(gatillo_i)
    texto_d.set_position((gatillo_d / 2 if gatillo_d > 10 else 12, 0.5))
    texto_i.set_position((gatillo_i / 2 if gatillo_i > 10 else 12, 0.5))
    texto_d.set_text(f"{gatillo_d:.0f}%")
    texto_i.set_text(f"{gatillo_i:.0f}%")

    fig.canvas.draw_idle()


try:
    while plt.fignum_exists(fig.number):
        actualizar()
        plt.pause(0.02)
except KeyboardInterrupt:
    pass
finally:
    puerto.close()
    plt.close("all")