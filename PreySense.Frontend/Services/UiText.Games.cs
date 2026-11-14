namespace PreySense.Frontend.Services;

internal static partial class UiText
{
    static UiText()
    {
        AddLightingDirections();
        AddLightingSettings();
        Add(Chinese, "游戏", "游戏模式", "添加游戏", "移除", "电源配置", "选择游戏可执行文件",
            "为每款游戏选择已保存的电源配置。游戏获得焦点时应用，退出时恢复。");
        Add(Hindi, "गेम", "गेम मोड", "गेम जोड़ें", "हटाएँ", "पावर प्रोफ़ाइल", "गेम की निष्पादन योग्य फ़ाइल चुनें",
            "हर गेम के लिए सहेजी गई पावर प्रोफ़ाइल चुनें। गेम पर फ़ोकस होने पर लागू होगी और बंद होने पर पिछली प्रोफ़ाइल लौटेगी।");
        Add(Spanish, "Juegos", "Modo de juego", "Añadir juego", "Eliminar", "Perfil de energía", "Seleccionar el ejecutable del juego",
            "Elige un perfil de energía guardado para cada juego. Se aplica al enfocarlo y se restaura al cerrarlo.");
        Add(French, "Jeux", "Mode jeu", "Ajouter un jeu", "Supprimer", "Profil d’alimentation", "Choisir l’exécutable du jeu",
            "Choisissez un profil d’alimentation enregistré pour chaque jeu. Il s’applique lorsque le jeu prend le focus et est restauré à sa fermeture.");
        Add(Turkish, "Oyunlar", "Oyun Modu", "Oyun Ekle", "Kaldır", "Güç Profili", "Oyun yürütülebilir dosyasını seçin",
            "Her oyun için kaydedilmiş bir güç profili seçin. Oyun odaklandığında uygulanır; kapandığında önceki profil geri yüklenir.");
        Chinese["Enable CPU Boost Tuning"] = "启用 CPU 睿频调校";
        Hindi["Enable CPU Boost Tuning"] = "CPU बूस्ट ट्यूनिंग सक्षम करें";
        Spanish["Enable CPU Boost Tuning"] = "Activar ajuste de CPU Boost";
        French["Enable CPU Boost Tuning"] = "Activer le réglage CPU Boost";
        Turkish["Enable CPU Boost Tuning"] = "CPU Boost ayarını etkinleştir";
    }

    private static void Add(Dictionary<string, string> table, string games, string mode, string add, string remove,
        string profile, string executable, string hint)
    {
        table["Games"] = games;
        table["Game Mode"] = mode;
        table["Add"] = add;
        table["Remove"] = remove;
        table["Power Profile"] = profile;
        table["Select a game executable"] = executable;
    }
}
