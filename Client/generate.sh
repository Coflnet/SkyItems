VERSION=0.14.0

docker run --rm -v "${PWD}:/local" --network host -u $(id -u ${USER}):$(id -g ${USER})  openapitools/openapi-generator-cli generate \
-i http://localhost:5014/swagger/v1/swagger.json \
-g csharp \
-o /local/out --additional-properties=packageName=Coflnet.Sky.Items.Client,packageVersion=$VERSION,licenseId=MIT

cd out
csProjPath=src/Coflnet.Sky.Items.Client/Coflnet.Sky.Items.Client.csproj
sed -i 's/GIT_USER_ID/Coflnet/g' $csProjPath
sed -i 's/GIT_REPO_ID/SkyItems/g' $csProjPath
sed -i 's/>OpenAPI/>Coflnet/g' $csProjPath

sed -i 's@annotations</Nullable>@annotations</Nullable>\n    <PackageReadmeFile>README.md</PackageReadmeFile>@g' $csProjPath
sed -i 's@Remove="System.Web" />@Remove="System.Web" />\n    <None Include="../../../../README.md" Pack="true" PackagePath="\"/>@g' $csProjPath

# correct enum values
FlagFile="src/Coflnet.Sky.Items.Client/Model/ItemFlags.cs"
sed -i 's/= 1/= 0/g' $FlagFile
sed -i 's/= 2/= 1/g' $FlagFile
sed -i 's/= 3/= 2/g' $FlagFile
sed -i 's/= 4/= 4/g' $FlagFile
sed -i 's/= 5/= 8/g' $FlagFile
sed -i 's/= 6/= 16/g' $FlagFile
sed -i 's/= 7/= 32/g' $FlagFile


echo updated $FlagFile
# correct enum values for categories
CategoryFile="src/Coflnet.Sky.Items.Client/Model/ItemCategory.cs"
sed -i 's/PETITEM/PET_ITEM/g' $CategoryFile
sed -i 's/REFORGESTONE/REFORGE_STONE/g' $CategoryFile
sed -i 's/TRAVELSCROLL/TRAVEL_SCROLL/g' $CategoryFile
sed -i 's/FISHINGROD/FISHING_ROD/g' $CategoryFile
sed -i 's/DUNGEONPASS/DUNGEON_PASS/g' $CategoryFile
sed -i 's/ARROWPOISON/ARROW_POISON/g' $CategoryFile
sed -i 's/FISHINGWEAPON/FISHING_WEAPON/g' $CategoryFile
sed -i 's/MINIONSKIN/MINION_SKIN/g' $CategoryFile
sed -i 's/PRIVATEISLAND/PRIVATE_ISLAND/g' $CategoryFile
sed -i 's/ISLANDCRYSTAL/ISLAND_CRYSTAL/g' $CategoryFile
sed -i 's/DUNGEONITEM/DUNGEON_ITEM/g' $CategoryFile
sed -i 's/DEEPCAVERNS/DEEP_CAVERNS/g' $CategoryFile
sed -i 's/TALISMANENRICHMENT/TALISMAN_ENRICHMENT/g' $CategoryFile
sed -i 's/THEFISH/THE_FISH/g' $CategoryFile
sed -i 's/PETSKIN/PET_SKIN/g' $CategoryFile
echo updated $CategoryFile

dotnet pack
cp src/Coflnet.Sky.Items.Client/bin/Release/Coflnet.Sky.Items.Client.*.nupkg ..
