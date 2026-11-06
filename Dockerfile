FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Booking.Api/Booking.Api.csproj src/Booking.Api/
COPY src/Booking.Domain/Booking.Domain.csproj src/Booking.Domain/
RUN dotnet restore src/Booking.Api/Booking.Api.csproj
COPY src/Booking.Api/ src/Booking.Api/
COPY src/Booking.Domain/ src/Booking.Domain/
RUN dotnet publish src/Booking.Api/Booking.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
# Use the existing Bash TCP support: no extra probe packages in the runtime image.
HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=5 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080; printf "GET /health/database HTTP/1.0\r\nHost: localhost\r\nConnection: close\r\n\r\n" >&3; read -r version status rest <&3; test "$status" = 200'
ENTRYPOINT ["dotnet", "Booking.Api.dll"]
