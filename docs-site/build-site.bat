export PATH="$PATH:/c/Users/jarja/.dotnet/tools"

# Build the docs
cd D:/Modulus-Game-Engine
docfx build docs-site/docfx.json

# Serve them
docfx serve docs-site/docs-site-out --port 8080