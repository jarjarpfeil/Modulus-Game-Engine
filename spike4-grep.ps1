$patterns = @('Module constructor','ModuleInit','ModuleRuntimeHelpers','ModuleCtor','AvailableAssemblySerializers','DataContractAliasMapping','GameScript','BackgroundScript','UIScript','CharacterScript','PlayAnimationScript','Scene ready','type not found','orphan','Could not load','Unable to','Activator','TypeLoad','Background','Level','Lane','SpaceEscape')
$lines = Get-Content 'D:\Modulus-Game-Engine\spike4-log.txt'
foreach ($p in $patterns) {
    $matches = $lines | Select-String -Pattern $p -CaseSensitive:$false
    if ($matches) {
        foreach ($m in $matches) {
            Write-Output "[$p] $($m.LineNumber): $($m.Line)"
        }
    }
}
