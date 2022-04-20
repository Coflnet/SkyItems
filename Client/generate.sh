VERSION=0.3.0

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5014/swagger/v1/swagger.json \
-g csharp-netcore \
-o /local/out --additional-properties=packageName=Coflnet.Sky.Items.Client,packageVersion=$VERSION,licenseId=MIT

cd out
sed -i 's/GIT_USER_ID/Coflnet/g' src/Coflnet.Sky.Items.Client/Coflnet.Sky.Items.Client.csproj
sed -i 's/GIT_REPO_ID/SkyItems/g' src/Coflnet.Sky.Items.Client/Coflnet.Sky.Items.Client.csproj
sed -i 's/>OpenAPI/>Coflnet/g' src/Coflnet.Sky.Items.Client/Coflnet.Sky.Items.Client.csproj

# correct enum values
FlagFile="src/Coflnet.Sky.Items.Client/Model/ItemFlags.cs"
sed -i 's/NONE = 1/NONE = 0/g' $FlagFile
sed -i 's/BAZAAR = 2/BAZAAR = 1/g' $FlagFile
sed -i 's/TRADEABLE = 3/TRADEABLE = 2/g' $FlagFile
sed -i 's/AUCTION = 4/AUCTION = 4/g' $FlagFile
sed -i 's/CRAFT = 5/CRAFT = 8/g' $FlagFile
sed -i 's/GLOWING = 6/GLOWING = 16/g' $FlagFile
sed -i 's/MUSEUM = 7/MUSEUM = 32/g' $FlagFile
echo updated $FlagFile

dotnet pack
cp src/Coflnet.Sky.Items.Client/bin/Debug/Coflnet.Sky.Items.Client.*.nupkg ..
