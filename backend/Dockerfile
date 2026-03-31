FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY RagChatbot.sln .
COPY RagChatbot.Api/*.csproj RagChatbot.Api/
COPY RagChatbot.Core/*.csproj RagChatbot.Core/
COPY RagChatbot.Infrastructure/*.csproj RagChatbot.Infrastructure/
COPY RagChatbot.Tests/*.csproj RagChatbot.Tests/
RUN dotnet restore RagChatbot.sln
COPY . .
RUN dotnet publish RagChatbot.Api/RagChatbot.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "RagChatbot.Api.dll"]