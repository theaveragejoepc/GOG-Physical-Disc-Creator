using GogDisc.Core;

// Opt-in integration check: the accompanying script mounts its own disposable ISO first.
if (args.Length == 2 && args[0] == "--eject-test-drive")
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try { GogDisc.Launcher.OpticalDriveEjector.EjectAsync(args[1]).GetAwaiter().GetResult(); }
        catch (Exception ex) { failure = ex; }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure is not null) throw failure;
    Console.WriteLine("Eject completed.");
    return 0;
}


if (args.Length >= 2 && args[0] == "--echo-arguments")
{
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(args.Skip(2).ToArray()));
    return 0;
}


if (args.Length >= 2 && args[0].Equals("--inspect-collection", StringComparison.OrdinalIgnoreCase))
{
    var collection = SetupCollectionScanner.Scan(args[1]);
    Console.WriteLine($"Collection: {collection.Games.Count} games ({collection.TotalBytes / 1024d / 1024d / 1024d:N2} GiB)");
    foreach (var game in collection.Games)
        Console.WriteLine($"  {game.Title}: {game.Family.InstallerFiles.Count} installer file(s), {game.Family.Extras.Count} extra(s), {(game.Family.InstallerBytes + game.Family.ExtrasBytes) / 1024d / 1024d:N1} MiB");
    return 0;
}

if (args.Length >= 2 && args[0].Equals("--inspect", StringComparison.OrdinalIgnoreCase))
{
    var extras = args.Length >= 3 && Directory.Exists(args[2]) ? args[2] : null;
    var capacity = args.Length >= 4 ? long.Parse(args[3]) : DiscPlanner.Bd50CapacityBytes;
    var family = SetupFamilyScanner.Scan(args[1], extras);
    var reserve = capacity is DiscPlanner.Cd650CapacityBytes or DiscPlanner.CdCapacityBytes
        ? DiscPlanner.CdReserveBytes : DiscPlanner.DefaultReserveBytes;
    var plan = DiscPlanner.Create(family, capacity, reserve);
    Console.WriteLine($"Setup family: {family.FamilyName}");
    Console.WriteLine($"Required files: {family.InstallerFiles.Count} ({family.InstallerBytes / 1024d / 1024d / 1024d:N2} GiB)");
    Console.WriteLine($"Extras: {family.Extras.Count} ({family.ExtrasBytes / 1024d / 1024d / 1024d:N2} GiB)");
    Console.WriteLine($"Excluded patches: {family.ExcludedPatches.Count}");
    Console.WriteLine($"Required discs: {plan.RequiredDiscCount}; total discs: {plan.Discs.Count}");
    foreach (var disc in plan.Discs)
        Console.WriteLine($"  Disc {disc.Number}: {disc.Role}, {disc.Files.Count} files, {disc.UsedBytes / 1024d / 1024d / 1024d:N2} GiB");
    return 0;
}

if (args.Length >= 2 && args[0].Equals("--launcher-smoke", StringComparison.OrdinalIgnoreCase))
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_smoke_1.0.exe", 256);
    var family = SetupFamilyScanner.Scan(setup);
    var plan = DiscPlanner.Create(family, 4096, 256);
    var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
    {
        Title = "Launcher Smoke Test",
        Version = "1.0",
        ProductType = PackageProductType.BaseGame,
        SetupFamily = family,
        Plan = plan,
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = args[1]
    });
    var disc = Path.Combine(result.PackageDirectory, "Launcher Smoke Test");
    var start = new System.Diagnostics.ProcessStartInfo(args[1]) { UseShellExecute = false };
    start.ArgumentList.Add("--self-test");
    start.ArgumentList.Add("--disc-root");
    start.ArgumentList.Add(disc);
    using var process = System.Diagnostics.Process.Start(start) ?? throw new Exception("Launcher did not start.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception($"Launcher smoke test exited with code {process.ExitCode}.");
    Console.WriteLine("Published launcher smoke test passed.");
    return 0;
}

var failures = new List<string>();
await Run("SteamGridDB API and artwork boundaries", ArtworkChecks.Run);
await Run("Encrypted artwork key storage", () =>
{
    using var fixture = new TempFixture();
    var path = Path.Combine(fixture.Root, "key.dat");
    Equal("", GogDisc.Packager.ArtworkKeyStore.Load(path));
    GogDisc.Packager.ArtworkKeyStore.Save(path, "  test-only-key-alpha  ");
    Equal("test-only-key-alpha", GogDisc.Packager.ArtworkKeyStore.Load(path));
    True(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-only-key-alpha"), "Key was stored as plaintext.");
    GogDisc.Packager.ArtworkKeyStore.Save(path, "test-only-key-beta");
    Equal("test-only-key-beta", GogDisc.Packager.ArtworkKeyStore.Load(path));
    Throws<ArgumentException>(() => GogDisc.Packager.ArtworkKeyStore.Save(path, " "));
    Equal("test-only-key-beta", GogDisc.Packager.ArtworkKeyStore.Load(path));
    File.WriteAllBytes(path, [1, 2, 3]);
    Throws<System.Security.Cryptography.CryptographicException>(() => GogDisc.Packager.ArtworkKeyStore.Load(path));
    GogDisc.Packager.ArtworkKeyStore.Forget(path);
    GogDisc.Packager.ArtworkKeyStore.Forget(path);
    Equal("", GogDisc.Packager.ArtworkKeyStore.Load(path));
    return Task.CompletedTask;
});
await Run("Installer queue and TargetFill", TestInstallerQueueAndPadding);
await Run("Direct game launch and cached Galaxy target repair", TestDirectLaunch);
await Run("DOSBox shortcut arguments and saved-target migration", () =>
{
    using var fixture = new TempFixture();
    var root = fixture.Directory("Retro Fixture");
    var exe = Path.Combine(root, "DOSBox.exe");
    File.WriteAllText(exe, "fixture; never executed");
    dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
    try
    {
        foreach (var suffix in new[] { "2", "3", "" })
        {
            dynamic link = shell.CreateShortcut(Path.Combine(root, $"Retro Fixture {suffix}".Trim() + ".lnk"));
            link.TargetPath = exe;
            link.Arguments = $"-conf \"..\\retro{suffix}.conf\" -conf \"..\\single{suffix}.conf\" -noconsole";
            link.WorkingDirectory = root;
            link.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        }
        foreach (var number in new[] { 1, 2, 3 })
        {
            var package = new PackageManifest { PackageId = "shortcut-test-" + Guid.NewGuid().ToString("N"), Title = $"Retro Fixture {number}" };
            try
            {
                InstallStateStore.Save(new InstallState { PackageId = package.PackageId, Title = package.Title, InstallLocation = root, PlayTarget = exe });
                var found = InstallDiscovery.Discover(package)!;
                var command = ProcessCommands.ForGame(found);
                var suffix = number == 1 ? "" : number.ToString();
                Equal($"-conf \"..\\retro{suffix}.conf\" -conf \"..\\single{suffix}.conf\" -noconsole", command.Arguments);
                Equal(root, command.WorkingDirectory);
                Equal(exe, command.FileName);
                Equal(command.Arguments, InstallStateStore.Load(package.PackageId)!.PlayArguments);
            }
            finally { File.Delete(AppPaths.PackageState(package.PackageId)); }
        }
        True(!InstallDiscovery.CanPlay(new InstallState { PlayTarget = exe }), "Bare DOSBox was considered playable.");
        True(InstallDiscovery.FindGameExecutable(["Retro Fixture"], root) is null, "Bare emulator selected.");
    }
    finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    return Task.CompletedTask;
});
await Run("Controller edges, repeat, dead zone and focus re-arm", () =>
{
    var input = new GogDisc.Launcher.ControllerButtons();
    Equal(0, input.Read(0x1000, 0, 0, true, 0));
    Equal(0, input.Read(0, 0, 0, true, 1));
    Equal(0x1000, input.Read(0x1000, 0, 0, true, 2));
    Equal(0, input.Read(0x1000, 0, 0, true, 3));
    Equal(0, input.Read(0, 12000, 0, true, 4));
    Equal(8, input.Read(0, 20000, 0, true, 5));
    Equal(0, input.Read(0, 20000, 0, true, 404));
    Equal(8, input.Read(0, 20000, 0, true, 405));
    Equal(0, input.Read(0x1000, 0, 0, false, 406));
    Equal(0, input.Read(0x1000, 0, 0, true, 407));
    Equal(0, input.Read(0, 0, 0, true, 408));
    Equal(0x2000, input.Read(0x2000, 0, 0, true, 409));
    return Task.CompletedTask;
});
await Run("Natural sorting", TestNaturalSorting);
await Run("Setup family scanning", TestSetupScanning);
await Run("Offline collection package", TestOfflineCollection);
await Run("Disc allocation", TestDiscAllocation);
await Run("Mixed media economy", TestMixedMediaEconomy);
await Run("Media inventory persistence", TestMediaInventoryPersistence);
await Run("Single-disc inventory selection", TestSingleDiscInventorySelection);
await Run("Package build and staging", TestBuildAndStage);
await Run("Disc swap waits for the drive", TestDriveSettle);
await Run("Path traversal rejection", TestPathSafety);
await Run("Read-only runtime cache", TestReadOnlyCache);
await Run("Locked and concurrent runtime cache", TestLockedCache);
await Run("Launcher argument round-trip", TestLauncherArguments);
await Run("Incomplete and gapped families rejected", TestIncompleteFamilies);
await Run("GOG Key Media package", TestKeyMediaPackage);
await Run("Key Media disc set per product", TestKeyMediaDiscSet);
await Run("GOG string size metadata", TestGogStringSizes);
await Run("GOG localized download estimate", TestGogLocalizedEstimate);
await Run("Owned Key Media uninstall", TestOwnedKeyUninstall);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} test(s) failed:");
    failures.ForEach(Console.Error.WriteLine);
    return 1;
}

Console.WriteLine("All self-tests passed.");
return 0;

async Task Run(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add($"FAIL {name}: {ex.Message}");
    }
}

async Task TestInstallerQueueAndPadding()
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_base.exe", 128);
    var patch1 = fixture.File("patch_game_1.exe", 64);
    var patch2 = fixture.File("patch_game_2.exe", 64);
    fixture.File("patch_game_2-1.bin", 100);
    var dlc1 = fixture.File("setup_dlc_a.exe", 100);
    var dlc2 = fixture.File("setup_dlc_b.exe", 100);
    var family = InstallerQueue.Combine(SetupFamilyScanner.Scan(setup), [patch1, patch2, dlc1, dlc2]);
    Equal(5, family.Installers.Count);
    Equal(6, family.InstallerFiles.Count);
    Equal(0, family.ExcludedPatches.Count);
    Throws<InvalidDataException>(() => InstallerQueue.Combine(family, [setup]));
    Throws<InvalidDataException>(() => InstallerQueue.Combine(family, [patch1, patch1]));
    var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
    {
        Title = "Queue", Version = "1", ProductType = PackageProductType.BaseGame,
        SetupFamily = family, Plan = DiscPlanner.Create(family, 4_000_000, 100_000),
        LauncherExecutable = fixture.File("Launch.exe", 100),
        OutputDirectory = fixture.Directory("output"), FillDiscSpace = true
    });
    var discRoot = Path.Combine(result.PackageDirectory, "Queue");
    var disc = DiscMedia.Load(discRoot);
    Equal(5, InstallerQueue.Validate(disc.Package).Count);
    var queueState = fixture.Directory("queue-state");
    Equal(0, InstallerQueue.Completed(disc.Package, queueState));
    InstallerQueue.SaveCompleted(disc.Package, queueState, 2);
    Equal(2, InstallerQueue.Completed(disc.Package, queueState));
    InstallerQueue.SaveCompleted(disc.Package, queueState, 999);
    Equal(5, InstallerQueue.Completed(disc.Package, queueState));
    var stage = fixture.Directory("stage");
    await StagingCopier.CopyDiscAsync(disc, stage, StagingStateStore.LoadOrCreate(stage, disc.Package), null, CancellationToken.None);
    foreach (var installer in disc.Package.Installers)
        True(File.Exists(SafePaths.ResolveUnderRoot(stage, installer)), "Queued installer was not staged.");
    True(!File.Exists(Path.Combine(stage, "00_lead_in_part1_filler.dat")), "Padding was staged as installer data.");
    var padded = Directory.EnumerateFiles(discRoot, "*", SearchOption.AllDirectories)
        .Sum(path => (new FileInfo(path).Length + 2047) / 2048 * 2048);
    True(padded <= 3_900_000 && 3_900_000 - padded < 2048, "Padding did not respect sector size and reserve.");
    disc.Package.Installers.Add("../outside.exe");
    Throws<InvalidDataException>(() => InstallerQueue.Validate(disc.Package));
    var overflow = fixture.Directory("overflow");
    File.WriteAllBytes(Path.Combine(overflow, "payload"), new byte[4096]);
    Throws<InvalidDataException>(() => TargetFill.Fill(overflow, 4096, 1024));
    var collision = fixture.Directory("collision");
    File.WriteAllText(Path.Combine(collision, "zz_lead_out_part2_filler.dat"), "keep me");
    Throws<IOException>(() => TargetFill.Fill(collision, 100_000, 4096));
    Equal("keep me", File.ReadAllText(Path.Combine(collision, "zz_lead_out_part2_filler.dat")));
    True(!File.Exists(Path.Combine(collision, "00_lead_in_part1_filler.dat")), "Failed padding was not cleaned up.");

    var root = fixture.Directory("collection");
    foreach (var name in new[] { "Game A", "Game B" })
    {
        var game = Path.Combine(root, name);
        Directory.CreateDirectory(Path.Combine(game, "DLC"));
        Directory.CreateDirectory(Path.Combine(game, "Patches"));
        File.Copy(setup, Path.Combine(game, "setup_base.exe"));
        File.Copy(dlc1, Path.Combine(game, "DLC", "setup_dlc.exe"));
        File.Copy(patch1, Path.Combine(game, "Patches", "patch_1.exe"));
        File.Copy(patch2, Path.Combine(game, "Patches", "patch_2.exe"));
        File.WriteAllText(Path.Combine(game, "cover.jpg"), "test artwork bytes");
        File.WriteAllLines(Path.Combine(game, "install-order.txt"), ["DLC/setup_dlc.exe", "Patches/patch_1.exe", "Patches/patch_2.exe"]);
    }
    var collection = SetupCollectionScanner.Scan(root);
    Equal(4, collection.Games[0].Family.Installers.Count);
    True(collection.Games[0].Family.Installers[1].EndsWith("setup_dlc.exe"), "Explicit order was ignored.");
    var built = await CollectionBuilder.BuildAsync(new CollectionBuildRequest
    {
        Title = "Collection", Version = "1", Collection = collection,
        CapacityBytes = 4_000_000, ReserveBytes = 100_000, MediaName = "test",
        OutputDirectory = fixture.Directory("collection-output"), LauncherExecutable = Path.Combine(fixture.Root, "Launch.exe"), FillDiscSpace = true
    });
    foreach (var game in built.Manifest.CollectionGames)
    {
        Equal(4, game.Installers.Count);
        True(File.Exists(Path.Combine(built.PackageDirectory, "Collection", game.CoverFile)), "Per-game cover was not copied.");
    }
    File.WriteAllText(Path.Combine(root, "Game A", "install-order.txt"), "../../outside.exe");
    Throws<InvalidDataException>(() => SetupCollectionScanner.Scan(root));
}

Task TestNaturalSorting()
{
    var values = new[] { "part-10.bin", "part-2.bin", "part-1.bin" };
    Array.Sort(values, NaturalStringComparer.Instance);
    Equal("part-1.bin,part-2.bin,part-10.bin", string.Join(',', values));
    return Task.CompletedTask;
}

Task TestSetupScanning()
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_example_1.0_(1).exe", 10);
    fixture.File("setup_example_1.0_(1)-2.bin", 20);
    fixture.File("setup_example_1.0_(1)-1.bin", 20);
    fixture.File("patch_example_0.9_to_1.0.exe", 5);
    var extras = fixture.Directory("extras");
    File.WriteAllText(Path.Combine(extras, "manual.txt"), "manual");
    var family = SetupFamilyScanner.Scan(setup, fixture.Root, true);
    Equal(3, family.InstallerFiles.Count);
    Equal("setup_example_1.0_(1)-1.bin", family.InstallerFiles[1].RelativePath);
    Equal(1, family.ExcludedPatches.Count);
    Equal(2, family.Extras.Count);
    return Task.CompletedTask;
}

async Task TestOfflineCollection()
{
    Equal("planescape torment enhanced edition", SetupNameParser.InferTitle("setup_planescape_torment_enhanced_edition_3.1.4.0_(26531).exe"));
    Equal("7 billion humans", SetupNameParser.InferTitle("setup_7_billion_humans_1.0_(12345).exe"));
    Equal("Planescape Torment Enhanced Edition", SetupNameParser.CollectionGameTitle(
        "Planescape Torment", "Enhanced Edition", "setup_planescape_torment_enhanced_edition_3.1.4.0_(26531).exe"));
    Equal("planescape torment", SetupNameParser.CollectionGameTitle(
        "My Favorites", "Enhanced Edition", "setup_planescape_torment_3.1.4.0_(26531).exe"));
    using var fixture = new TempFixture();
    var collectionRoot = fixture.Directory("DOOM");
    var first = Path.Combine(collectionRoot, "DOOM 1");
    var second = Path.Combine(collectionRoot, "DOOM 2");
    // Layout is a suggestion: the installer is found in any subfolder, and setups inside Extras are bonus content.
    var firstInstaller = Path.Combine(first, "Installers", "GOG");
    var secondBase = Path.Combine(second, "Base Game");
    Directory.CreateDirectory(firstInstaller);
    Directory.CreateDirectory(secondBase);
    File.WriteAllBytes(Path.Combine(firstInstaller, "setup_doom_1.9_(1).exe"), new byte[512]);
    File.WriteAllBytes(Path.Combine(secondBase, "setup_doom_ii_1.9_(2).exe"), new byte[640]);
    File.WriteAllBytes(Path.Combine(secondBase, "setup_doom_ii_1.9_(2)-1.bin"), new byte[1024]);
    var extras = Path.Combine(second, "Extras");
    Directory.CreateDirectory(extras);
    File.WriteAllText(Path.Combine(extras, "manual.txt"), "manual");
    File.WriteAllBytes(Path.Combine(extras, "setup_bonus_soundtrack.exe"), new byte[64]);

    var collection = SetupCollectionScanner.Scan(collectionRoot);
    Equal(2, collection.Games.Count);
    Equal("DOOM 1", collection.Games[0].Title);
    var result = await CollectionBuilder.BuildAsync(new CollectionBuildRequest
    {
        Title = "DOOM Collection",
        Version = "1.0",
        Collection = collection,
        CapacityBytes = 10_000,
        ReserveBytes = 256,
        MediaName = "Test media",
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = fixture.File("Launch.exe", 128)
    });

    var discRoot = Path.Combine(result.PackageDirectory, "DOOM Collection");
    CheckAutorun(discRoot, "DOOM Collection");
    var media = DiscMedia.Load(discRoot);
    Equal(2, media.Package.CollectionGames.Count);
    Equal("DOOM 1", media.Package.CollectionGames[0].InstallDetectionNames.Single());
    Equal("DOOM 2", media.Package.CollectionGames[1].InstallDetectionNames.Single());
    Equal(4, media.Package.Files.Count);
    True(File.Exists(Path.Combine(discRoot, media.Package.CollectionGames[1].InstallerRelativePath)),
        "A collection game installer was not packaged.");
    True(File.Exists(Path.Combine(discRoot, media.Package.CollectionGames[1].ExtrasRelativePath, "manual.txt")),
        "Per-game extras were not packaged.");
    True(media.Package.CollectionGames.Select(game => game.GameId).Distinct().Count() == 2,
        "Collection games did not receive distinct install-state identities.");
}

Task TestDiscAllocation()
{
    Equal(700_000_000L, DiscPlanner.CdCapacityBytes);
    Equal(96L * 1024 * 1024, DiscPlanner.CdReserveBytes);
    Equal(4_700_000_000L, DiscPlanner.Dvd5CapacityBytes);
    Equal(8_500_000_000L, DiscPlanner.Dvd9CapacityBytes);
    Equal(100_000_000_000L, DiscPlanner.Bd100CapacityBytes);
    Equal(128_000_000_000L, DiscPlanner.Bd128CapacityBytes);
    var family = new SetupFamily
    {
        SetupExecutable = "setup.exe",
        FamilyName = "setup",
        InstallerFiles =
        [
            new("a", "setup.exe", 10, PackageFileKind.Installer, 0),
            new("b", "setup-1.bin", 55, PackageFileKind.Installer, 1),
            new("c", "setup-2.bin", 55, PackageFileKind.Installer, 2)
        ],
        Extras = [new("d", "manual.pdf", 20, PackageFileKind.Extra, 0)],
        ExcludedPatches = []
    };
    var plan = DiscPlanner.Create(family, 100, 10);
    Equal(2, plan.RequiredDiscCount);
    Equal(2, plan.Discs.Count);
    Equal(PackageFileKind.Extra, plan.Discs[1].Files[^1].Kind);

    var cdSizedFamily = new SetupFamily
    {
        SetupExecutable = "setup_breath_of_fire_iv.exe",
        FamilyName = "setup_breath_of_fire_iv",
        InstallerFiles =
        [
            new("game", "setup_breath_of_fire_iv.exe", 558_467_216, PackageFileKind.Installer, 0)
        ],
        Extras = [new("manual", "Manual.pdf", 9_538_566, PackageFileKind.Extra, 0)],
        ExcludedPatches = []
    };
    var cdPlan = DiscPlanner.Create(
        cdSizedFamily,
        DiscPlanner.CdCapacityBytes,
        DiscPlanner.CdReserveBytes);
    Equal(1, cdPlan.RequiredDiscCount);
    Equal(1, cdPlan.Discs.Count);
    return Task.CompletedTask;
}

Task TestMixedMediaEconomy()
{
    var inventory = MediaCatalog.ParseInventory("BD50 x1, BD25 x10");
    var suggestion = MediaCatalog.Suggest(53_000_000_000L, inventory);
    Equal(2, suggestion.Discs.Count);
    Equal("BD50", suggestion.Discs[0].Id);
    Equal("BD25", suggestion.Discs[1].Id);

    // Five smaller discs have less nominal capacity, but two burns are the more economical choice.
    var dvdInventory = MediaCatalog.ParseInventory("DVD9 x2, DVD5 x1, CD700 x3");
    var dvdSuggestion = MediaCatalog.Suggest(14_000_000_000L, dvdInventory);
    Equal(2, dvdSuggestion.Discs.Count);
    True(dvdSuggestion.Discs.All(disc => disc.Id == "DVD9"),
        "The optimizer preferred five smaller discs over two DVD-9 discs.");
    var dvdOptions = MediaCatalog.SuggestOptions(14_000_000_000L, dvdInventory);
    True(dvdOptions.Any(option => option.Discs.Count == 5),
        "The five-disc capacity-saving alternative was not offered to the user.");
    Equal(2, dvdOptions.Single(option => option.Discs.Count == 2).SuggestedCaseCapacity);
    Equal(6, dvdOptions.Single(option => option.Discs.Count == 5).SuggestedCaseCapacity);

    var family = new SetupFamily
    {
        SetupExecutable = "setup.exe",
        FamilyName = "setup",
        InstallerFiles = [new("a", "setup.exe", 80, PackageFileKind.Installer, 0)],
        Extras = [],
        ExcludedPatches = []
    };
    var media = new[]
    {
        new OpticalMediaType("BIG", "Big disc", 70, 10),
        new OpticalMediaType("SMALL", "Small disc", 40, 10)
    };
    var plan = DiscPlanner.CreateMixed(family, media);
    Equal(2, plan.Discs.Count);
    Equal("Big disc", plan.Discs[0].MediaName);
    Equal("Small disc", plan.Discs[1].MediaName);
    True(plan.Discs.SelectMany(disc => disc.Files).All(file => file.PartCount == 2), "Mixed-media file was not split across both discs.");
    return Task.CompletedTask;
}

Task TestMediaInventoryPersistence()
{
    using var fixture = new TempFixture();
    Equal(Path.Combine("profile", "GOG Disc Packager", "media-inventory.json"),
        MediaInventoryStore.GetPath("profile"));
    var path = Path.Combine(fixture.Root, "settings", "media-inventory.json");
    MediaInventoryStore.Save(path,
    [
        new MediaInventoryItem(MediaCatalog.Dvd9, 3),
        new MediaInventoryItem(MediaCatalog.Bd25, 7),
        new MediaInventoryItem(MediaCatalog.Cd700, 0)
    ]);
    var loaded = MediaInventoryStore.Load(path);
    Equal(2, loaded.Count);
    Equal(3, loaded.Single(item => item.Media.Id == "DVD9").Count);
    Equal(7, loaded.Single(item => item.Media.Id == "BD25").Count);
    var remaining = MediaInventoryStore.Consume(loaded, [MediaCatalog.Dvd9, MediaCatalog.Bd25, MediaCatalog.Bd25]);
    Equal(2, remaining.Single(item => item.Media.Id == "DVD9").Count);
    Equal(5, remaining.Single(item => item.Media.Id == "BD25").Count);
    File.WriteAllText(path, "not json");
    Equal(0, MediaInventoryStore.Load(path).Count);
    return Task.CompletedTask;
}

async Task TestBuildAndStage()
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_tiny_1.0_(1).exe", 512);
    fixture.File("setup_tiny_1.0_(1)-1.bin", 2048);
    fixture.File("setup_tiny_1.0_(1)-2.bin", 2048);
    var launcher = fixture.File("Launch.exe", 128);
    var background = fixture.File("background.jpg", 96);
    var cover = fixture.File("cover.png", 96);
    var family = SetupFamilyScanner.Scan(setup);
    var plan = DiscPlanner.Create(family, 3500, 256);
    Equal(2, plan.RequiredDiscCount);
    var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
    {
        Title = "Tiny Game",
        Version = "1.0",
        ProductType = PackageProductType.BaseGame,
        SetupFamily = family,
        Plan = plan,
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = launcher,
        BackgroundImage = background,
        CoverImage = cover
    });
    Equal(2, result.Manifest.RequiredDiscCount);
    Equal(2, result.Manifest.DiscLayout.Count);
    var discOne = Path.Combine(result.PackageDirectory, "Tiny Game - Disc 1");
    var media = DiscMedia.Load(discOne);
    var staging = fixture.Directory("staging");
    var state = StagingStateStore.LoadOrCreate(staging, result.Manifest);
    await StagingCopier.CopyDiscAsync(media, staging, state, null, CancellationToken.None);
    True(File.Exists(Path.Combine(staging, "setup_tiny_1.0_(1).exe")), "Setup was not staged.");
    var discTwo = Path.Combine(result.PackageDirectory, "Tiny Game - Disc 2");
    await StagingCopier.CopyDiscAsync(DiscMedia.Load(discTwo), staging, state, null, CancellationToken.None);
    Equal(2048L, new FileInfo(Path.Combine(staging, "setup_tiny_1.0_(1)-2.bin")).Length);
    True(result.Manifest.Files.Any(file => file.PartCount > 1), "Oversized file was not split across discs.");
    CheckAutorun(discOne, "Tiny Game");
    CheckAutorun(discTwo, "Tiny Game");
    True(File.ReadAllText(Path.Combine(result.PackageDirectory, "BURNING-INSTRUCTIONS.txt")).Contains("Disc 01: 0 GB media"),
        "Burning instructions did not identify the required media.");
    Equal(3500L, media.Disc.CapacityBytes);
    Equal("background.jpg", result.Manifest.BackgroundFile);
    Equal("cover.png", result.Manifest.CoverFile);
    True(File.Exists(Path.Combine(discOne, "cover.png")), "Cover art was not packaged.");
}

// A disc inserted mid-install is listed before the drive can read it. The copy has to outlast that, while media that
// is genuinely wrong still fails.
async Task TestDriveSettle()
{
    using var fixture = new TempFixture();
    var setup = fixture.File("setup_tiny_1.0_(1).exe", 512);
    fixture.File("setup_tiny_1.0_(1)-1.bin", 2048);
    fixture.File("setup_tiny_1.0_(1)-2.bin", 2048);
    var family = SetupFamilyScanner.Scan(setup);
    var result = await PackageBuilder.BuildAsync(new PackageBuildRequest
    {
        Title = "Tiny Game",
        Version = "1.0",
        ProductType = PackageProductType.BaseGame,
        SetupFamily = family,
        Plan = DiscPlanner.Create(family, 3500, 256),
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = fixture.File("Launch.exe", 128),
        BackgroundImage = fixture.File("background.jpg", 96),
        CoverImage = fixture.File("cover.png", 96)
    });

    var discTwo = DiscMedia.Load(Path.Combine(result.PackageDirectory, "Tiny Game - Disc 2"));
    var entry = discTwo.Disc.Files.First(file => file.Kind == PackageFileKind.Installer);
    var payload = SafePaths.ResolveUnderRoot(discTwo.Root, entry.DiscPath);
    var unreadable = payload + ".spinning-up";

    var staging = fixture.Directory("staging");
    var state = StagingStateStore.LoadOrCreate(staging, result.Manifest);
    await StagingCopier.CopyDiscAsync(DiscMedia.Load(Path.Combine(result.PackageDirectory, "Tiny Game - Disc 1")),
        staging, state, null, CancellationToken.None);

    File.Move(payload, unreadable);
    var settles = Task.Run(async () =>
    {
        await Task.Delay(1200);
        File.Move(unreadable, payload);
    });
    await StagingCopier.CopyDiscAsync(discTwo, staging, state, null, CancellationToken.None);
    await settles;
    Equal(2048L, new FileInfo(Path.Combine(staging, "setup_tiny_1.0_(1)-2.bin")).Length);

    var previousTimeout = StagingCopier.DriveSettleTimeout;
    StagingCopier.DriveSettleTimeout = TimeSpan.FromSeconds(1);
    try
    {
        File.Move(payload, unreadable);
        var refused = false;
        var second = fixture.Directory("staging-2");
        try
        {
            await StagingCopier.CopyDiscAsync(discTwo, second,
                StagingStateStore.LoadOrCreate(second, result.Manifest), null, CancellationToken.None);
        }
        catch (IOException) { refused = true; }
        True(refused, "A disc file that never appears was not reported as missing.");
    }
    finally
    {
        StagingCopier.DriveSettleTimeout = previousTimeout;
        File.Move(unreadable, payload);
    }
}

Task TestPathSafety()
{
    using var fixture = new TempFixture();
    var rejected = false;
    try { SafePaths.ResolveUnderRoot(fixture.Root, "..\\outside.exe"); }
    catch (InvalidDataException) { rejected = true; }
    True(rejected, "Traversal path was accepted.");
    return Task.CompletedTask;
}

Task TestReadOnlyCache()
{
    using var fixture = new TempFixture();
    var source = fixture.File("disc-package.json", 128);
    var destination = Path.Combine(fixture.Directory("runtime"), "package.json");
    File.SetAttributes(source, File.GetAttributes(source) | FileAttributes.ReadOnly);
    CacheFiles.Copy(source, destination);
    True((File.GetAttributes(destination) & FileAttributes.ReadOnly) == 0, "Cached file inherited the disc's read-only attribute.");
    File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);
    CacheFiles.Copy(source, destination);
    True((File.GetAttributes(destination) & FileAttributes.ReadOnly) == 0, "Existing read-only cache file was not repaired.");
    File.SetAttributes(source, File.GetAttributes(source) & ~FileAttributes.ReadOnly);
    return Task.CompletedTask;
}

async Task TestLockedCache()
{
    using var fixture = new TempFixture();
    var source = fixture.File("Launch.exe", 4096);
    var destination = Path.Combine(fixture.Directory("runtime"), "Launch.exe");
    var locked = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
    var release = Task.Run(async () => { await Task.Delay(350); locked.Dispose(); });
    try { await Task.Run(() => CacheFiles.Copy(source, destination)); }
    finally { await release; locked.Dispose(); }
    True(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(destination)), "Cache copy did not recover after a file lock.");
    File.Delete(destination);
    await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => CacheFiles.Copy(source, destination))));
    True(File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(destination)), "Concurrent cache writes damaged the launcher.");
    True(!Directory.EnumerateFiles(Path.GetDirectoryName(destination)!, "*.tmp").Any(), "Cache left temporary files.");
}

async Task TestLauncherArguments()
{
    using var fixture = new TempFixture();
    var output = Path.Combine(fixture.Root, "arguments.json");
    var executable = Environment.ProcessPath!;
    var start = ProcessCommands.ForLocalLauncher(executable, @"C:\GOG Disc Tool\package.json", @"G:\");
    if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        start.ArgumentList.Insert(0, System.Reflection.Assembly.GetExecutingAssembly().Location);
    var index = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    start.ArgumentList.Insert(index, "--echo-arguments");
    start.ArgumentList.Insert(index + 1, output);
    using var child = System.Diagnostics.Process.Start(start)!;
    await child.WaitForExitAsync();
    Equal(0, child.ExitCode);
    var received = System.Text.Json.JsonSerializer.Deserialize<string[]>(File.ReadAllText(output))!;
    True(received.SequenceEqual(new[] { "--local", "--package", @"C:\GOG Disc Tool\package.json", "--disc-root", @"G:\" }), "Windows changed a launcher argument (especially the drive-root trailing slash).");
}

Task TestIncompleteFamilies()
{
    using (var incomplete = new TempFixture())
    {
        var setup = incomplete.File("setup_bad_1.0.exe", 10);
        incomplete.File("download.bin.part", 10);
        Throws<InvalidDataException>(() => SetupFamilyScanner.Scan(setup));
    }
    using (var gapped = new TempFixture())
    {
        var setup = gapped.File("setup_bad_1.0.exe", 10);
        gapped.File("setup_bad_1.0-1.bin", 10);
        gapped.File("setup_bad_1.0-3.bin", 10);
        Throws<InvalidDataException>(() => SetupFamilyScanner.Scan(setup));
    }
    return Task.CompletedTask;
}

Task TestDirectLaunch()
{
    using var fixture = new TempFixture();
    var root = fixture.Directory("Dungeons3");
    var game = Path.Combine(root, "Dungeons3.exe");
    File.WriteAllText(game, "fixture only; never executed");
    File.WriteAllText(Path.Combine(root, "unins000.exe"), "fixture");
    File.WriteAllText(Path.Combine(root, "setup.exe"), "fixture");
    var galaxy = fixture.File("GalaxyClient.exe", 32);
    True(!InstallDiscovery.IsDirectGameExecutable(galaxy), "Galaxy was accepted as a game.");
    Equal(game, InstallDiscovery.FindGameExecutable(["Dungeons 3"], root, galaxy));
    var launcherOnly = fixture.Directory("client-only");
    File.WriteAllText(Path.Combine(launcherOnly, "GalaxyClient.exe"), "fixture");
    True(InstallDiscovery.FindGameExecutable(["Dungeons 3"], launcherOnly) is null, "Library client chosen as a fallback.");
    var package = new PackageManifest { PackageId = "direct-launch-test-" + Guid.NewGuid().ToString("N"), Title = "Dungeons 3" };
    try
    {
        InstallStateStore.Save(new InstallState { PackageId = package.PackageId, Title = package.Title, InstallLocation = root, PlayTarget = galaxy });
        var repaired = InstallDiscovery.Discover(package);
        Equal(game, repaired!.PlayTarget);
        Equal(game, InstallStateStore.Load(package.PackageId)!.PlayTarget);
        Throws<InvalidDataException>(() => InstallDiscovery.SaveManualTarget(package, galaxy));
    }
    finally { File.Delete(AppPaths.PackageState(package.PackageId)); }
    return Task.CompletedTask;
}

void CheckAutorun(string discRoot, string title)
{
    var lines = File.ReadAllLines(Path.Combine(discRoot, "autorun.inf"));
    foreach (var expected in new[] { "[AutoRun]", "label=" + title, "open=Launch.exe", "icon=game.ico" })
        True(lines.Contains(expected), $"Missing autorun entry: {expected}");
    True(File.Exists(Path.Combine(discRoot, "Launch.exe")), "Autorun launcher is missing.");
    True(File.Exists(Path.Combine(discRoot, "game.ico")), "Autorun icon is missing.");
}

async Task TestKeyMediaPackage()
{
    using var fixture = new TempFixture();
    var launcher = fixture.File("Launch.exe", 128);
    var result = await KeyMediaBuilder.BuildAsync(new KeyMediaBuildRequest
    {
        Discs = [new KeyMediaDisc
        {
            Product = new GogKeyProduct { ProductId = "1091507383", Slug = "test_game", Title = "Test Game", Language = "en", AvailableExtras = 0 }
        }],
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = launcher
    });
    Equal(PackageDeploymentType.GogKeyMedia, result.Manifest.DeploymentType);
    Equal("1091507383", result.Manifest.GogKeyProduct!.ProductId);
    Equal(0, result.Manifest.GogKeyProduct.AvailableExtras);
    var mediaRoot = Path.Combine(result.PackageDirectory, "Test Game (Game Key)");
    CheckAutorun(mediaRoot, "Test Game");
    var media = DiscMedia.Load(mediaRoot);
    Equal(0, media.Package.Files.Count);
    True(!Directory.Exists(Path.Combine(mediaRoot, "Payload")), "Key Media unexpectedly contains a payload folder.");
    True(File.ReadAllText(Path.Combine(mediaRoot, "package.json")).Contains("1091507383"), "Product identity was not written.");
}

async Task TestKeyMediaDiscSet()
{
    using var fixture = new TempFixture();
    var launcher = Path.Combine(fixture.Root, "Launch.exe");
    File.WriteAllText(launcher, "launcher");
    var baseProduct = new GogKeyProduct { ProductId = "1423049311", Slug = "base_game", Title = "Base Game", Language = "en" };
    var addOn = new GogKeyProduct
    {
        ProductId = "1256837418", Slug = "base_game", Title = "Story Add-On", Language = "en",
        DiscRole = KeyDiscRole.Dlc, BaseProductId = "1423049311", BaseTitle = "Base Game", IncludedDlcs = ["1256837418"]
    };
    var result = await KeyMediaBuilder.BuildAsync(new KeyMediaBuildRequest
    {
        Discs = [new KeyMediaDisc { Product = baseProduct }, new KeyMediaDisc { Product = addOn }],
        OutputDirectory = fixture.Directory("output"),
        LauncherExecutable = launcher
    });

    var discOne = Path.Combine(result.PackageDirectory, "Base Game (Game Key) - Disc 1");
    var discTwo = Path.Combine(result.PackageDirectory, "Base Game (Game Key) - Disc 2");
    True(Directory.Exists(discOne), "Disc 1 folder was not created.");
    True(Directory.Exists(discTwo), "Disc 2 folder was not created.");

    CheckAutorun(discOne, "Base Game");
    CheckAutorun(discTwo, "Story Add-On");
    var addOnPackage = DiscMedia.Load(discTwo).Package;
    Equal("gog-1256837418", addOnPackage.PackageId);
    Equal(KeyDiscRole.Dlc, addOnPackage.GogKeyProduct!.DiscRole);
    // gogdl addresses DLC through the base game, so the add-on disc must download the base product ID.
    Equal("1423049311", addOnPackage.GogKeyProduct.DownloadProductId);
    // The add-on installs into the base game's folder, so it must detect that game, not itself.
    True(addOnPackage.InstallDetectionNames.Contains("Base Game"), "Add-on disc does not detect the base game.");
    Equal("1423049311", DiscMedia.Load(discOne).Package.GogKeyProduct!.DownloadProductId);
}

Task TestGogStringSizes()
{
    using var plain = System.Text.Json.JsonDocument.Parse("\"105300000000\"");
    True(GogDlRuntime.TryReadBytes(plain.RootElement, out var plainBytes), "Numeric string was not accepted.");
    Equal(105_300_000_000L, plainBytes);
    using var units = System.Text.Json.JsonDocument.Parse("\"105.3 GB\"");
    True(GogDlRuntime.TryReadBytes(units.RootElement, out var unitBytes), "Human-readable size was not accepted.");
    Equal(105_300_000_000L, unitBytes);
    return Task.CompletedTask;
}

Task TestGogLocalizedEstimate()
{
    var json = "{\"size\":{\"*\":{\"download_size\":188,\"disk_size\":424},\"en-US\":{\"download_size\":451552662,\"disk_size\":585344616}}}";
    var estimate = GogDlRuntime.ParseDownloadEstimate(json, "en");
    Equal(451_552_850L, estimate.DownloadBytes);
    Equal(585_345_040L, estimate.InstalledBytes);
    return Task.CompletedTask;
}

Task TestOwnedKeyUninstall()
{
    using var fixture = new TempFixture();
    var install = fixture.Directory("key-install");
    File.WriteAllText(Path.Combine(install, "game.exe"), "game");
    var package = new PackageManifest
    {
        PackageId = "gog-12345", Title = "Test", DeploymentType = PackageDeploymentType.GogKeyMedia,
        GogKeyProduct = new GogKeyProduct { ProductId = "12345", Slug = "test", Title = "Test" }
    };
    KeyInstallOwnership.Mark(install, package);
    True(KeyInstallOwnership.IsOwned(install, package), "Key installation marker was not accepted.");
    KeyInstallOwnership.Remove(install, package);
    True(!Directory.Exists(install), "Owned Key Media installation was not removed.");
    return Task.CompletedTask;
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}.");
}

static void True(bool value, string message)
{
    if (!value) throw new Exception(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

Task TestSingleDiscInventorySelection()
{
    var inventory = MediaCatalog.ParseInventory("BD50 x1, DVD9 x2, BD25 x1");
    Equal("DVD9", MediaCatalog.SuggestSingle(8_000_000_000L, inventory).Id);
    Equal("BD25", MediaCatalog.SuggestSingle(9_000_000_000L, inventory).Id);

    try
    {
        MediaCatalog.SuggestSingle(60_000_000_000L, inventory);
        throw new Exception("An oversized collection unexpectedly received a single-disc suggestion.");
    }
    catch (InvalidDataException ex)
    {
        True(ex.Message.Contains("No single disc", StringComparison.Ordinal),
            "The single-disc inventory failure did not explain the constraint.");
    }
    return Task.CompletedTask;
}

sealed class TempFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "GogDiscSelfTests", Guid.NewGuid().ToString("N"));
    public TempFixture() => System.IO.Directory.CreateDirectory(Root);

    public string Directory(string name)
    {
        var path = Path.Combine(Root, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    public string File(string name, int size)
    {
        var path = Path.Combine(Root, name);
        System.IO.File.WriteAllBytes(path, Enumerable.Range(0, size).Select(index => (byte)(index % 251)).ToArray());
        return path;
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(Root, true); } catch { }
    }
}
