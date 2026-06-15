📡 EXECUTION PLAN 2: Mod Syncing & Multiplayer Handshake (Post-v1)

Context for the Agent: Multiplayer mod syncing in Modulus relies on Desynced Mod Lists. We never send compiled assemblies (.dll or .so) over the network to prevent Remote Code Execution (RCE) vulnerabilities and save bandwidth. Instead, the client and server exchange a cryptographic manifest handshake.
Step 1: The Handshake Payload Definition

    Target: Network packet definitions.

    Task: Create a ClientModManifestPacket containing a list of records: [ModId (string), Version (string), AssemblyHash (SHA-256 string)].

    Task: Create a ConnectionRejectedPacket containing: [Reason (string), MissingDependencies (List<string>), OptionalResolutionUrl (string)].

Step 2: Server Mod Policy Configuration

    Target: A new mod-policy.toml parser for the dedicated server.

    Task: Implement reading for:

        Network.Security.AllowNativeMods (bool)

        ServerSecurity.AllowUnlistedClientMods (bool)

        RequiredMods (List of ID, VersionRule, and MatchHash boolean)

        BlacklistedMods (List of exact IDs and Hashes)

Step 3: Semantic Versioning (SemVer) Parser

    Target: Modulus.Modding.Api.Versioning (New utility).

    Task: Implement a lightweight SemVer evaluator that can take a client's version (e.g., 1.1.4) and check it against a Server VersionRule.

        Support ^ (Major match: ^1.1.0 allows 1.2.0 but not 2.0.0)

        Support ~ (Patch match: ~1.1.0 allows 1.1.4 but not 1.2.0)

        Support Exact Match (default behavior if no prefix is given).

Step 4: The Handshake Evaluator Pipeline

    Target: Server-side connection authorization logic.

    Task: When ClientModManifestPacket is received, execute this pipeline:

        Native Check: If the client manifest contains any mod with RequiresNativeCode == true, and server AllowNativeMods == false, KICK.

        Note: On the engine side, RequiresNativeCode maps to ModSecurityConfig.EnableNativeModLoading. The server's mod-policy.toml AllowNativeMods corresponds to this setting. Native mods bypass ALC isolation (loaded into non-collectible NativeModLoadContext), cannot be hot-swapped, and their native DLL handles remain resident until process exit.

        Blacklist Check: If any client mod ID or Hash exists in BlacklistedMods, KICK ("Unauthorized modification detected").

        Integrity Check: Loop through server RequiredMods. If the client is missing the ID, fails the SemVer rule, or fails the Hash check (if MatchHash = true), KICK ("Missing/Mismatched required mod").

        Unlisted Check: If the client has extra mods not on the server list, and AllowUnlistedClientMods == false, KICK ("Client-side mods are disabled on this server").

        Pass: Accept connection.

Step 5: Mod Developer Intent (Manifest Addition)

    Target: ModManifest.cs

    Task: Add public NetworkCompatibilityLevel NetworkCompatibility { get; set; } = Exact; to the mod manifest schema. Options: Exact, PatchOnly, MajorOnly, ClientSideOnly. This allows the server to automatically generate VersionRules based on the mod developer's recommendation.