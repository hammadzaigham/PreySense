namespace PreySense.Frontend.Services;

internal static partial class UiText
{
    static UiText()
    {
        Chinese["Game Profiles"] = "游戏配置";
        Hindi["Game Profiles"] = "गेम प्रोफ़ाइल";
        Spanish["Game Profiles"] = "Perfiles de juego";
        French["Game Profiles"] = "Profils de jeu";
        Turkish["Game Profiles"] = "Oyun Profilleri";
        AddLightingDirections();
        AddFanDiagnostics();
        Chinese["LCD Overdrive"] = "LCD 响应加速";
        Hindi["LCD Overdrive"] = "LCD ओवरड्राइव";
        Spanish["LCD Overdrive"] = "Sobremarcha LCD";
        French["LCD Overdrive"] = "Overdrive LCD";
        Turkish["LCD Overdrive"] = "LCD Overdrive";
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
        Chinese["CPU Boost Tuning"] = "CPU 睿频调校";
        Hindi["CPU Boost Tuning"] = "CPU बूस्ट ट्यूनिंग";
        Spanish["CPU Boost Tuning"] = "Ajuste de CPU Boost";
        French["CPU Boost Tuning"] = "Réglage CPU Boost";
        Turkish["CPU Boost Tuning"] = "CPU Boost Ayarı";
        Chinese["Apply CPU boost"] = "应用 CPU 睿频";
        Hindi["Apply CPU boost"] = "CPU बूस्ट लागू करें";
        Spanish["Apply CPU boost"] = "Aplicar boost de CPU";
        French["Apply CPU boost"] = "Appliquer le boost CPU";
        Turkish["Apply CPU boost"] = "CPU boost uygula";

        string[] extraKeys =
        [
            "No overlay slots configured. Click 'Add Slot' to add metrics.",
            "Tuning is busy. Try again in a moment.",
            "Turn on Hardware Tuning first.",
            "Reset Mode",
            "Enter a PROCHOT offset, or turn its switch off.",
            "Set CPU limits and time limit before applying.",
            "Set GPU offsets before applying.",
            "Discrete GPU Power Cycle",
            "Hardware Discrete GPU",
            "Aggressive",
            "Efficient Enabled",
            "Efficient Aggressive",
            "Aggressive At Guaranteed",
            "Efficient Aggressive At Guaranteed",
            "Apply CPU power limits",
            "Apply GPU clock offsets",
            "Apply GPU power limits",
            "GPU Delta",
            "GPU Dynamic Boost",
        ];
        string[][] extraText =
        [
            [
                "未配置浮窗指标栏。点击“添加栏”以添加指标。",
                "调校繁忙，请稍后再试。",
                "请先开启硬件调校。",
                "重置模式",
                "请输入 PROCHOT 偏移，或关闭其开关。",
                "应用前请设置 CPU 限制和时间限制。",
                "应用前请设置 GPU 偏移。",
                "独显电源循环",
                "硬件独立显卡",
                "激进",
                "高效启用",
                "高效激进",
                "保证频率下激进",
                "保证频率下高效激进",
                "应用 CPU 功耗限制",
                "应用 GPU 频率偏移",
                "应用 GPU 功耗限制",
                "GPU 增量",
                "GPU 动态加速",
            ],
            [
                "कोई ओवरले स्लॉट कॉन्फ़िगर नहीं है। मेट्रिक्स जोड़ने के लिए 'स्लॉट जोड़ें' पर क्लिक करें।",
                "ट्यूनिंग व्यस्त है। कुछ देर बाद पुनः प्रयास करें।",
                "पहले हार्डवेयर ट्यूनिंग चालू करें।",
                "मोड रीसेट करें",
                "PROCHOT ऑफ़सेट दर्ज करें या उसका स्विच बंद करें।",
                "लागू करने से पहले CPU सीमाएँ और समय सीमा सेट करें।",
                "लागू करने से पहले GPU ऑफ़सेट सेट करें।",
                "डिस्क्रीट GPU पावर साइकिल",
                "हार्डवेयर डिस्क्रीट GPU",
                "आक्रामक",
                "कुशल सक्षम",
                "कुशल आक्रामक",
                "गारंटीड पर आक्रामक",
                "गारंटीड पर कुशल आक्रामक",
                "CPU पावर सीमाएँ लागू करें",
                "GPU क्लॉक ऑफ़सेट लागू करें",
                "GPU पावर सीमाएँ लागू करें",
                "GPU डेल्टा",
                "GPU डायनामिक बूस्ट",
            ],
            [
                "No hay ranuras configuradas. Haz clic en 'Añadir ranura' para agregar métricas.",
                "El ajuste está ocupado. Inténtalo de nuevo en un momento.",
                "Activa primero el ajuste de hardware.",
                "Restablecer modo",
                "Introduce un desplazamiento PROCHOT o desactiva su interruptor.",
                "Configura los límites de CPU y el límite de tiempo antes de aplicar.",
                "Configura los desplazamientos de GPU antes de aplicar.",
                "Ciclo de energía de GPU dedicada",
                "GPU dedicada por hardware",
                "Agresivo",
                "Eficiente activado",
                "Eficiente agresivo",
                "Agresivo en garantizado",
                "Eficiente agresivo en garantizado",
                "Aplicar límites de potencia de CPU",
                "Aplicar desplazamientos de reloj de GPU",
                "Aplicar límites de potencia de GPU",
                "Delta de GPU",
                "Dynamic Boost de GPU",
            ],
            [
                "Aucun emplacement configuré. Cliquez sur « Ajouter un emplacement » pour ajouter des mesures.",
                "Le réglage est occupé. Réessayez dans un instant.",
                "Activez d’abord le réglage matériel.",
                "Réinitialiser le mode",
                "Saisissez un décalage PROCHOT ou désactivez son interrupteur.",
                "Définissez les limites CPU et la limite de temps avant d’appliquer.",
                "Définissez les décalages GPU avant d’appliquer.",
                "Cycle d’alimentation du GPU dédié",
                "GPU dédié matériel",
                "Agressif",
                "Efficace activé",
                "Efficace agressif",
                "Agressif au garanti",
                "Efficace agressif au garanti",
                "Appliquer les limites de puissance CPU",
                "Appliquer les décalages d’horloge GPU",
                "Appliquer les limites de puissance GPU",
                "Delta GPU",
                "Dynamic Boost GPU",
            ],
        ];
        var extraTables = new[] { Chinese, Hindi, Spanish, French };
        for (int lang = 0; lang < extraTables.Length; lang++)
            for (int i = 0; i < extraKeys.Length; i++)
                extraTables[lang][extraKeys[i]] = extraText[lang][i];
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
