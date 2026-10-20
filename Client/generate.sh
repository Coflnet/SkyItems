#!/usr/bin/env bash
set -euo pipefail

# Single source of truth for the published client version. Bump this (and merge to main) to
# cut a release: the Package GitHub Actions workflow triggers on changes to this file.
VERSION=0.21.3
PACKAGE_NAME=Coflnet.Sky.Items.Client

# Where to read the OpenAPI document from. Defaults to a locally running API (the historical
# workflow: start the app with `dotnet run`, then run this script). Can also be a file path,
# e.g. one produced without starting any dependent services (used by CI).
SWAGGER_INPUT="${SWAGGER_INPUT:-http://localhost:5014/swagger/v1/swagger.json}"
# Generator image pinned by tag AND digest so output is reproducible; override only for testing.
GENERATOR_IMAGE="${GENERATOR_IMAGE:-openapitools/openapi-generator-cli:v7.25.0@sha256:2ab0a9680222de65dc9d3baf861aa02b99e1b80c211d8221ebf3ae8f8a102524}"

docker_mounts=(-v "${PWD}:/local")
if [[ "$SWAGGER_INPUT" =~ ^https?:// ]]; then
  generator_input="$SWAGGER_INPUT"
else
  # Local file: bind-mount it at a fixed in-container path so its location on the host (which
  # may be outside $PWD, e.g. a file produced elsewhere in a CI job) doesn't matter.
  swagger_abs="$(cd "$(dirname "$SWAGGER_INPUT")" && pwd)/$(basename "$SWAGGER_INPUT")"
  docker_mounts+=(-v "${swagger_abs}:/swagger-input.json:ro")
  generator_input="/swagger-input.json"
fi

docker run --rm "${docker_mounts[@]}" --network host -u "$(id -u):$(id -g)" "$GENERATOR_IMAGE" generate \
-i "$generator_input" \
-g csharp \
-o /local/out --additional-properties=packageName=$PACKAGE_NAME,packageVersion=$VERSION,licenseId=MIT,targetFramework=net8.0,library=restsharp

cd out
path=src/$PACKAGE_NAME/$PACKAGE_NAME.csproj
sed -i 's/GIT_USER_ID/Coflnet/g' $path
sed -i 's/GIT_REPO_ID/SkyItems/g' $path
sed -i 's/>OpenAPI/>Coflnet/g' $path
sed -i 's@annotations</Nullable>@annotations</Nullable>\n    <PackageReadmeFile>README.md</PackageReadmeFile>\n    <PublishRepositoryUrl>true</PublishRepositoryUrl>@g' $path
# Force the exact repository URL a verifier reads from the .nuspec (RepositoryType is already
# "git" via the generator's own GIT_USER_ID/GIT_REPO_ID template above).
sed -i 's@<RepositoryUrl>.*</RepositoryUrl>@<RepositoryUrl>https://github.com/Coflnet/SkyItems</RepositoryUrl>@' $path
# Pack the README. Anchored on the (always-present, unique) closing </PropertyGroup> tag instead
# of a line number: the generator rewrites this file from scratch on every run, so a line number
# drifts the moment the template gains or loses a line, silently dropping the README from the package.
sed -i '/<\/PropertyGroup>/a\
\
  <ItemGroup>\
    <None Include="..\/..\/..\/..\/README.md" Pack="true" PackagePath="\\"\/>\
  </ItemGroup>' $path

# correct enum values
FlagFile="src/$PACKAGE_NAME/Model/ItemFlags.cs"
sed -i 's/= 1/= 0/g' $FlagFile
sed -i 's/= 2/= 1/g' $FlagFile
sed -i 's/= 3/= 2/g' $FlagFile
sed -i 's/= 4/= 4/g' $FlagFile
sed -i 's/= 6/= 16/g' $FlagFile
sed -i 's/= 8/= 64/g' $FlagFile
sed -i 's/= 5/= 8/g' $FlagFile
sed -i 's/= 7/= 32/g' $FlagFile
sed -i 's/    public enum ItemFlags/    [Flags]\n    public enum ItemFlags/g' $FlagFile

echo updated $FlagFile
# correct enum values for categories
CategoryFile="src/$PACKAGE_NAME/Model/ItemCategory.cs"
sed -i 's/PETITEM/PET_ITEM/g' $CategoryFile
sed -i 's/REFORGESTONE/REFORGE_STONE/g' $CategoryFile
sed -i 's/TRAVELSCROLL/TRAVEL_SCROLL/g' $CategoryFile
sed -i 's/FISHINGRODPART/FISHING_ROD_PART/g' $CategoryFile
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
sed -i 's/CARNIVALMASK/CARNIVAL_MASK/g' $CategoryFile
sed -i 's/FISHINGNET/FISHING_NET/g' $CategoryFile
sed -i 's/CARNIVALMASK/CARNIVAL_MASK/g' $CategoryFile
sed -i 's/WATERINGCAN/WATERING_CAN/g' $CategoryFile
sed -i 's/FARMINGTOOL/FARMING_TOOL/g' $CategoryFile
sed -i 's/GARDENCHIP/GARDEN_CHIP/g' $CategoryFile
echo updated $CategoryFile

pack_args=()
if [[ -n "${GITHUB_SHA:-}" ]]; then
  # Embeds <repository type="git" url="..." commit="$GITHUB_SHA"/> in the .nuspec so a verifier
  # can tie the published package back to the exact commit it was built from.
  pack_args+=(-p:RepositoryCommit="$GITHUB_SHA")
fi
dotnet pack "${pack_args[@]}"
cp src/$PACKAGE_NAME/bin/Release/$PACKAGE_NAME.*.nupkg ..
rm -f *.sln

# Packing only. Publishing to nuget.org is done by the Package GitHub Actions workflow
# (.github/workflows/package.yml) with NuGet trusted publishing; nothing here pushes.
# To publish by hand, run `dotnet nuget push` on the .nupkg yourself.
