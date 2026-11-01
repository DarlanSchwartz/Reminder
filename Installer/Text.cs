using System.Collections.Generic;
using System.Globalization;

namespace ReminderSetup;

// Setup text in English, Spanish and Portuguese, chosen from the Windows display language.
internal static class Text
{
    private static readonly int Index = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName switch { "es" => 1, "pt" => 2, _ => 0 };

    public static string T(string key) => Table.TryGetValue(key, out var text) ? text[Index] : key;

    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["Version"] = ["Version {0}", "Versión {0}", "Versão {0}"],
        ["Location"] = ["Install location", "Ubicación de instalación", "Local de instalação"],
        ["Change"] = ["Change", "Cambiar", "Alterar"],
        ["Options"] = ["Options", "Opciones", "Opções"],
        ["Desktop"] = ["Desktop shortcut", "Acceso directo en el escritorio", "Atalho na área de trabalho"],
        ["PathOption"] = ["Add the reminder command", "Agregar el comando reminder", "Adicionar o comando reminder"],
        ["PathHint"] = ["Lets scripts and AI agents create tasks", "Permite que scripts y agentes de IA creen tareas", "Permite que scripts e agentes de IA criem tarefas"],
        ["Launch"] = ["Open Reminder when finished", "Abrir Reminder al terminar", "Abrir o Reminder ao terminar"],
        ["Runtime"] = ["Reminder needs the .NET 10 Desktop Runtime, which isn't installed yet.", "Reminder necesita .NET 10 Desktop Runtime, que aún no está instalado.", "O Reminder precisa do .NET 10 Desktop Runtime, que ainda não está instalado."],
        ["GetRuntime"] = ["Download", "Descargar", "Baixar"],
        ["Install"] = ["Install", "Instalar", "Instalar"],
        ["Update"] = ["Update", "Actualizar", "Atualizar"],
        ["Cancel"] = ["Cancel", "Cancelar", "Cancelar"],
        ["Close"] = ["Close", "Cerrar", "Fechar"],
        ["Open"] = ["Open Reminder", "Abrir Reminder", "Abrir o Reminder"],
        ["Installing"] = ["Installing Reminder…", "Instalando Reminder…", "Instalando o Reminder…"],
        ["Preparing"] = ["Preparing…", "Preparando…", "Preparando…"],
        ["Shortcuts"] = ["Creating shortcuts…", "Creando accesos directos…", "Criando atalhos…"],
        ["Installed"] = ["Reminder is ready", "Reminder está listo", "O Reminder está pronto"],
        ["InstalledText"] = ["Find it in the Start menu. Your tasks stay on this PC.", "Lo encontrarás en el menú Inicio. Tus tareas se quedan en este PC.", "Ele está no menu Iniciar. Suas tarefas ficam neste PC."],
        ["Failed"] = ["Something went wrong", "Algo salió mal", "Algo deu errado"],
        ["Retry"] = ["Try again", "Reintentar", "Tentar de novo"],
        ["FolderTitle"] = ["Choose where to install Reminder", "Elige dónde instalar Reminder", "Escolha onde instalar o Reminder"],
        ["UninstallTitle"] = ["Remove Reminder?", "¿Quitar Reminder?", "Remover o Reminder?"],
        ["UninstallText"] = ["The app, its shortcuts and its scheduled alarms will be removed.", "Se quitarán la aplicación, sus accesos directos y sus alarmas programadas.", "O aplicativo, seus atalhos e seus alarmes agendados serão removidos."],
        ["Keep"] = ["Keep my tasks and songs", "Conservar mis tareas y canciones", "Manter minhas tarefas e músicas"],
        ["KeepHint"] = ["Reinstalling later brings them back", "Si reinstalas, volverán", "Ao reinstalar, elas voltam"],
        ["Uninstall"] = ["Uninstall", "Desinstalar", "Desinstalar"],
        ["Uninstalling"] = ["Removing Reminder…", "Quitando Reminder…", "Removendo o Reminder…"],
        ["Alarms"] = ["Removing scheduled alarms…", "Quitando alarmas programadas…", "Removendo alarmes agendados…"],
        ["Files"] = ["Removing files…", "Quitando archivos…", "Removendo arquivos…"],
        ["Uninstalled"] = ["Reminder was removed", "Reminder se quitó", "O Reminder foi removido"],
        ["UninstalledText"] = ["Thanks for using it.", "Gracias por usarlo.", "Obrigado por usar."],
        ["UninstalledKept"] = ["Your tasks were kept in {0}.", "Tus tareas se conservaron en {0}.", "Suas tarefas foram mantidas em {0}."],
    };
}
