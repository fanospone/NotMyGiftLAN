
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["NotMyGiftLAN.csproj", "./"]
RUN dotnet restore "NotMyGiftLAN.csproj"

COPY . .
RUN dotnet publish "NotMyGiftLAN.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080

ENTRYPOINT ["dotnet", "NotMyGiftLAN.dll"]