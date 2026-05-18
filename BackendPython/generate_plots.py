import pandas as pd
import matplotlib.pyplot as plt
import seaborn as sns
from app import IMAGE_FOLDER

FIGSIZE = (15, 9)
FONTSIZE = 30
FONTSIZE_LABEL = 24
FONTSIZE_TICK = 16

def save_fig(filename):
    plt.savefig(IMAGE_FOLDER + "/" + filename, dpi=100)

def generate_plots(file_path):
    df = pd.read_csv(file_path, quotechar='"')

    df.columns = df.columns.str.replace(r'\s+', ' ', regex=True).str.strip()

    cols_to_numeric = df.columns.drop(['Date', 'Time'])
    for col in cols_to_numeric:
        df[col] = pd.to_numeric(df[col], errors='coerce')

    df['Datetime_str'] = df['Date'] + ' ' + df['Time'] + '.' + df['Millisecond'].astype(int).astype(str).str.zfill(3)
    df['Datetime'] = pd.to_datetime(df['Datetime_str'], format='%Y/%m/%d %H:%M:%S.%f')

    df = df.drop(columns=['Date', 'Time', 'Millisecond', 'Datetime_str'])
    df.set_index('Datetime', inplace=True)

    sns.set_theme(style="whitegrid")

    # ==========================================
    # WYKRES 1: Parametry napędu (Prędkość, Prąd, Obciążenie, Moment)
    # ==========================================
    plt.figure(figsize=FIGSIZE)
    plt.title('Prędkość w funkcji czasu', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Prędkość [m/min]', color='blue')
    plt.ylabel('Prędkość\n[m/min]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)

    plt.tight_layout()
    save_fig('01_predkosc.png')
    plt.close()

    plt.figure(figsize=FIGSIZE)
    plt.title('Prąd w funkcji czasu', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Prąd - składowa rzeczywista [A]', color='red')
    plt.ylabel('Prąd [A]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)

    plt.tight_layout()
    save_fig('01_prad.png')
    plt.close()

    plt.figure(figsize=FIGSIZE)
    plt.title('Obciążenie w funkcji czasu', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Obciążenie [%]', color='green')
    plt.ylabel('Obciążenie [%]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)

    plt.tight_layout()
    save_fig('01_obciazenie.png')
    plt.close()

    plt.figure(figsize=FIGSIZE)
    plt.title('Moment obrotowy w funkcji czasu', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Moment obrotowy [kNm]', color='purple')
    plt.ylabel('Moment\n[kNm]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)

    plt.tight_layout()
    save_fig('01_moment.png')
    plt.close()

    # ==========================================
    # WYKRES 2: Siła nacisku (Prawa vs Lewa Strona)
    # ==========================================
    plt.figure(figsize=FIGSIZE)
    plt.title('Siła Nacisku - Strona Prawa vs Lewa', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Siła nacisku - strona prawa [kN]', label='Prawa strona', color='orange')
    sns.lineplot(data=df, x=df.index, y='Siła nacisku - strona lewa [kN]', label='Lewa strona', color='teal', alpha=0.7)
    plt.ylabel('Siła nacisku [kN]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)
    plt.legend()
    plt.tight_layout()
    save_fig('02_sily_nacisku.png')
    plt.close()

    # ==========================================
    # WYKRES 3: Nastawa pionowa walców (Szczelina)
    # ==========================================
    plt.figure(figsize=FIGSIZE)
    plt.title('Nastawa Pionowa Walca (Szczelina)', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Nastawa pionowa walca - strona prawa [mm]', label='Prawa strona', color='darkred')
    sns.lineplot(data=df, x=df.index, y='Nastawa pionowa walca - strona lewa [mm]', label='Lewa strona', color='darkblue', linestyle='--')
    plt.ylabel('Nastawa walca [mm]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)
    plt.legend()
    plt.tight_layout()
    save_fig('03_nastawy_walcow.png')
    plt.close()

    # ==========================================
    # WYKRES 4: Temperatura przed i za walcarką
    # ==========================================
    plt.figure(figsize=FIGSIZE)
    plt.title('Temperatura Wlewka Przed i Za Walcarką', fontsize=FONTSIZE)
    sns.lineplot(data=df, x=df.index, y='Temperatura wlewka przed walcarką [C]', label='Przed walcarką', color='crimson')
    sns.lineplot(data=df, x=df.index, y='Temperatura wlewka za walcarką [C]', label='Za walcarką', color='navy')
    plt.ylabel('Temperatura [°C]', fontsize=FONTSIZE_LABEL)
    plt.xlabel('Czas', fontsize=FONTSIZE_LABEL)
    plt.tick_params(axis='both', which='major', labelsize=FONTSIZE_TICK)
    plt.legend()
    plt.tight_layout()
    save_fig('04_temperatury.png')
    plt.close()