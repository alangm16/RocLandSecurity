using RocLandSecurity.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using RocLandSecurity.Models;

namespace RocLandSecurity.Views.Supervisor
{
    public partial class admGuardias : ContentPage
    {
        private readonly SupervisorService _service;
        public ObservableCollection<Usuario> Usuarios { get; } = new();

        public admGuardias()
        {
            InitializeComponent();
        }

        
    }
}
