#if LINUX
using System;
using System.IO;

namespace Configuration_Management.Services
{
    /// <summary>
    /// Определение окружений, где непрерывная перерисовка, прозрачность и анимации дороги:
    /// программный рендер (нет аппаратного GPU-ускорения), виртуализация и X11 без
    /// композитинга. Используется всеми окнами Avalonia-ветки, чтобы не дублировать
    /// детект и не расходиться в поведении (issue #153).
    /// <para>
    /// Два независимых флага:
    /// <list type="bullet">
    /// <item><see cref="OpaqueWindow"/> — окно рисуется полностью непрозрачным (прозрачность,
    /// blur и полупрозрачная «стеклянная» подложка отключаются);</item>
    /// <item><see cref="DisableAnimations"/> — непрерывные анимации и плавные переходы
    /// отключаются (бесконечный индетерминантный индикатор, Transitions), которые на
    /// программном рендере держат цикл перерисовки и дают высокую нагрузку CPU.</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class LinuxRendering
    {
        /// <summary>
        /// Рисовать окно непрозрачным. На X11 без композитора прозрачное окно заставляет
        /// оконный менеджер непрерывно перерисовывать фон; в виртуализации и при программном
        /// рендере это же даёт высокую нагрузку CPU (issue #153). Прозрачность остаётся только
        /// на Wayland, где композитор обязателен и постоянной перерисовки фона нет.
        /// </summary>
        public static bool OpaqueWindow =>
            ForceOpaqueEnv() || Virtualized || SoftwareRender || NoCompositorAssumed;

        /// <summary>
        /// Рисовать ли диалог/окно простым прямоугольником без расширения клиентской
        /// области и без прозрачных полей под скругление/тень. В непрозрачном режиме
        /// <see cref="ExtendClientAreaToDecorationsHint"/> у безрамкового окна тоже требует
        /// прозрачной маски на X11 без композитора — а она и есть источник постоянной
        /// перерисовки фона (issue #153). Признак используется окнами как с системной
        /// рамкой, так и без неё.
        /// </summary>
        public static bool PlainOpaqueWindow => OpaqueWindow;

        /// <summary>
        /// Композитор на сессии не гарантирован. На X11 композитор может отсутствовать
        /// (нет Composite Manager), тогда любое полупрозрачное или расширенное окно
        /// заставляет X-сервер непрерывно перерисовывать фон — «зависание» и ~36% CPU
        /// (issue #153). На Wayland композитор обязателен, поэтому там прозрачность безопасна.
        /// Положительный ответ консервативен: на X11 считается, что композитора может
        /// не быть, и прозрачность/расширение отключаются по умолчанию.
        /// </summary>
        public static bool NoCompositorAssumed { get; } = !IsWayland();

        /// <summary>
        /// Отключить непрерывные анимации и плавные переходы. Включается на программном
        /// рендере, в виртуализации и на X11 без композитора: там каждый кадр анимации
        /// целиком рисуется софтом (а на X11 без композитора даже обычная компоновка
        /// кадра идёт без GPU-ускорения), а бесконечные индикаторы (индетерминантный
        /// ProgressBar, hover-переходы) держат рендер-цикл постоянно занятым, что
        /// проявляется как ~36% CPU и «зависание» реакции на мышь (issue #153).
        /// </summary>
        public static bool DisableAnimations => SoftwareRender || Virtualized || NoCompositorAssumed;

        /// <summary>Программный рендер: нет аппаратного GPU-драйвера либо он задан принудительно.</summary>
        public static bool SoftwareRender { get; } = DetectSoftwareRender();

        /// <summary>Виртуализация по признакам гипервизора в системе.</summary>
        public static bool Virtualized { get; } = DetectVm();

        /// <summary>
        /// Пишет в лог расширенную диагностику окружения рендеринга (issue #153): все
        /// вычисленные флаги окна/анимаций и релевантные переменные окружения. Вызывается
        /// один раз при старте приложения, чтобы на машине пользователя (VirtualBox/KDE
        /// NEON) было видно, какой режим выбран и почему, а не гадать по симптомам.
        /// </summary>
        public static void LogStartupDiagnostics(IAppLogger logger)
        {
            try
            {
                var driDrivers = ReadDriGpuDrivers();
                var sb = new System.Text.StringBuilder()
                    .Append("Диагностика окружения рендеринга: ")
                    .Append("Virtualized=").Append(Virtualized)
                    .Append(", SoftwareRender=").Append(SoftwareRender)
                    .Append(", NoCompositorAssumed=").Append(NoCompositorAssumed)
                    .Append(", OpaqueWindow=").Append(OpaqueWindow)
                    .Append(", DisableAnimations=").Append(DisableAnimations)
                    .Append("; DRI-драйверы=[").Append(driDrivers).Append(']');
                logger.Info(sb.ToString());

                // Переменные окружения сессии — источник первопричины зависания на X11
                // без композитора (XDG_SESSION_TYPE/WAYLAND_DISPLAY/DISPLAY) и выбора
                // программного рендера (Mesa/Gallium).
                logger.Info(
                    "Переменные окружения: XDG_SESSION_TYPE=" + Env("XDG_SESSION_TYPE") +
                    ", WAYLAND_DISPLAY=" + Env("WAYLAND_DISPLAY") +
                    ", DISPLAY=" + Env("DISPLAY") +
                    ", GALLIUM_DRIVER=" + Env("GALLIUM_DRIVER") +
                    ", MESA_LOADER_DRIVER_OVERRIDE=" + Env("MESA_LOADER_DRIVER_OVERRIDE") +
                    ", LIBGL_ALWAYS_SOFTWARE=" + Env("LIBGL_ALWAYS_SOFTWARE"));
            }
            catch
            {
                // Диагностика не должна ронять запуск.
            }
        }

        /// <summary>Возвращает значение переменной окружения (пустая строка, если её нет).</summary>
        private static string Env(string name)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name) ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Пользователь явно запросил непрозрачное окно переменной окружения
        /// <c>CM_DISABLE_TRANSPARENCY=1</c> (запасной ручной способ диагностики, issue #153).
        /// </summary>
        private static bool ForceOpaqueEnv()
        {
            try
            {
                var v = Environment.GetEnvironmentVariable("CM_DISABLE_TRANSPARENCY");
                return v is not null
                    && (v.Trim() == "1" || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Признак виртуализации: флаг hypervisor/qemu/kvm в /proc/cpuinfo, DMI-данные
        /// о вендоре (VirtualBox/QEMU/VMware/KVM/Hyper-V), загруженные гостовые модули ядра
        /// и драйверы виртуальных GPU в /sys/class/drm. Консервативный детектор: на
        /// остальном железе считается, что виртуализации нет (issue #153).
        /// </summary>
        private static bool DetectVm()
        {
            try
            {
                if (File.Exists("/proc/cpuinfo"))
                {
                    var cpuInfo = File.ReadAllText("/proc/cpuinfo");
                    if (cpuInfo.Contains("hypervisor", StringComparison.OrdinalIgnoreCase)
                        || cpuInfo.Contains("qemu", StringComparison.OrdinalIgnoreCase)
                        || cpuInfo.Contains("kvm", StringComparison.OrdinalIgnoreCase)
                        || cpuInfo.Contains("virtualbox", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                foreach (var dmiPath in new[]
                         {
                             "/sys/devices/virtual/dmi/id/sys_vendor",
                             "/sys/devices/virtual/dmi/id/product_name",
                             "/sys/devices/virtual/dmi/id/board_vendor"
                         })
                {
                    if (!File.Exists(dmiPath))
                        continue;
                    var value = File.ReadAllText(dmiPath);
                    if (value.Contains("VirtualBox", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("innotek", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("QEMU", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("VMware", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("KVM", StringComparison.OrdinalIgnoreCase)
                        || value.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // Гостевые модули/драйверы гипервизоров, загруженные в ядро. Проверка по
                // каталогу /sys/module надёжнее поиска по имени процесса: у VirtualBox в
                // гостевой системе почти всегда есть vboxguest/vboxsf/vboxvideo.
                if (Directory.Exists("/sys/module"))
                {
                    foreach (var module in Directory.EnumerateDirectories("/sys/module"))
                    {
                        var name = Path.GetFileName(module);
                        if (name.Contains("vbox", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("vmw", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("qemu", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("virtio", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("qxl", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("bochs", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("xen", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }

                // Виртуальные GPU-драйверы из /sys/class/drm/*/uevent (DRIVER=...): они
                // появляются у VirtualBox (vboxvideo), QEMU (qxl, bochs-drm, virtio_gpu)
                // и VMware (vmwgfx), поэтому прямо указывают на виртуализацию даже там,
                // где DMI/процессор признаков не дают (настройки гостя).
                var driDrivers = ReadDriGpuDrivers();
                if (driDrivers.Length > 0
                    && (driDrivers.Contains("qxl", StringComparison.OrdinalIgnoreCase)
                        || driDrivers.Contains("vmwgfx", StringComparison.OrdinalIgnoreCase)
                        || driDrivers.Contains("virtio_gpu", StringComparison.OrdinalIgnoreCase)
                        || driDrivers.Contains("bochs", StringComparison.OrdinalIgnoreCase)
                        || driDrivers.Contains("vboxvideo", StringComparison.OrdinalIgnoreCase)
                        || driDrivers.Contains("qemu", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
            catch
            {
                // При любой ошибке чтения не мешаем запуску.
            }
            return false;
        }

        /// <summary>
        /// Собирает имена драйверов GPU из <c>/sys/class/drm/*/device/uevent</c>
        /// (поле DRIVER=…; в самом card*N/uevent его нет — там только
        /// MAJOR/MINOR/DEVNAME/DEVTYPE). Используется и для распознавания виртуализации
        /// по виртуальным GPU, и для диагностики окружения рендеринга. Возвращает
        /// пустую строку, если данные недоступны.
        /// </summary>
        private static string ReadDriGpuDrivers()
        {
            try
            {
                var drivers = new System.Collections.Generic.List<string>();
                if (Directory.Exists("/sys/class/drm"))
                {
                    foreach (var card in Directory.EnumerateDirectories("/sys/class/drm"))
                    {
                        if (!Path.GetFileName(card).StartsWith("card", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var uevent = Path.Combine(card, "device", "uevent");
                        if (!File.Exists(uevent))
                            continue;
                        foreach (var rawLine in File.ReadAllLines(uevent))
                        {
                            if (rawLine.StartsWith("DRIVER=", StringComparison.OrdinalIgnoreCase))
                            {
                                var driver = rawLine.Substring("DRIVER=".Length).Trim();
                                if (driver.Length > 0 && !drivers.Contains(driver, StringComparer.OrdinalIgnoreCase))
                                    drivers.Add(driver);
                            }
                        }
                    }
                }
                return string.Join(", ", drivers);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Пользователь явно запросил программный рендер переменной окружения
        /// <c>CM_FORCE_SOFTWARE_RENDER=1</c> (запасной ручной способ диагностики, issue #153).
        /// </summary>
        private static bool ForceSoftwareRenderEnv()
        {
            try
            {
                var v = Environment.GetEnvironmentVariable("CM_FORCE_SOFTWARE_RENDER");
                return v is not null
                    && (v.Trim() == "1" || v.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Признак программного рендера. Источники: переменные окружения Mesa/Gallium
        /// (llvmpipe/softpipe, LIBGL_ALWAYS_SOFTWARE, MESA_LOADER_DRIVER_OVERRIDE),
        /// Vulkan software-ICD (lavapipe), явный флаг <c>CM_FORCE_SOFTWARE_RENDER=1</c>
        /// (запасной ручной способ, issue #153), а также отсутствие аппаратных узлов
        /// рендеринга /dev/dri/renderD* (признак того, что GPU-ускорения в сессии нет).
        /// Последний признак консервативен — при сомнении считаем рендер программным.
        /// </summary>
        private static bool DetectSoftwareRender()
        {
            if (ForceSoftwareRenderEnv())
                return true;

            try
            {
                var gallium = Environment.GetEnvironmentVariable("GALLIUM_DRIVER");
                if (gallium?.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) == true
                    || gallium?.Contains("softpipe", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
                var loader = Environment.GetEnvironmentVariable("MESA_LOADER_DRIVER_OVERRIDE");
                if (loader?.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) == true
                    || loader?.Contains("softpipe", StringComparison.OrdinalIgnoreCase) == true)
                    return true;
                var alwaysSoftware = Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE");
                if (alwaysSoftware is not null
                    && (alwaysSoftware.Trim() == "1"
                        || alwaysSoftware.Trim().Equals("true", StringComparison.OrdinalIgnoreCase)))
                    return true;
                var vkIcd = Environment.GetEnvironmentVariable("VK_ICD_FILENAMES");
                if (!string.IsNullOrWhiteSpace(vkIcd)
                    && (vkIcd.Contains("lavapipe", StringComparison.OrdinalIgnoreCase)
                        || vkIcd.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (Directory.Exists("/dev/dri"))
                {
                    try
                    {
                        var driFiles = Directory.GetFiles("/dev/dri", "renderD*");
                        if (driFiles.Length == 0)
                            return true;
                    }
                    catch
                    {
                        // Каталог есть, но не читается — считаем программным.
                        return true;
                    }
                }
            }
            catch
            {
                // При любой ошибке — программный рендер (консервативно).
                return true;
            }
            return false;
        }

        /// <summary>
        /// Сессия Wayland (композитор обязателен, прозрачность безопасна). Признак
        /// считается установленным только при согласованных показателях: тип сессии
        /// (<c>XDG_SESSION_TYPE</c>) содержит «wayland» И задана <c>WAYLAND_DISPLAY</c>.
        /// <para>
        /// Если задана только <c>WAYLAND_DISPLAY</c>, а тип сессии не Wayland, это,
        /// как правило, X11-приложение в XWayland: реальное окно создаётся на X11,
        /// где прозрачность и расширение клиентской области вызывают нативный abort
        /// при открытии диалога (issue #168). Требование согласованности не даёт
        /// ошибочно считать такой случай безопасным Wayland.
        /// </para>
        /// </summary>
        private static bool IsWayland()
        {
            try
            {
                var hasDisplay = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
                var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
                var waylandSession = !string.IsNullOrWhiteSpace(sessionType)
                    && sessionType.Contains("wayland", StringComparison.OrdinalIgnoreCase);
                return hasDisplay && waylandSession;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif