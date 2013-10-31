﻿using AutoMapper;
using BlowTrial.Domain.Providers;
using BlowTrial.Infrastructure.Interfaces;
using BlowTrial.Models;
using BlowTrial.Properties;
using BlowTrial.View;
using MvvmExtraLite.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Threading;

namespace BlowTrial.ViewModel
{
    public class AllParticipantsViewModel : WorkspaceViewModel
    {
        #region Fields
        ParticipantUpdateView _updateWindow;
        ParticipantUpdateViewModel _selectedParticipant;
        #endregion // Fields

        #region Constructor

        public AllParticipantsViewModel(IRepository repository):base(repository)
        {
            base.DisplayName = Strings.AllParticipantsViewModel_DisplayName;
            _repository.ParticipantAdded += OnParticipantAdded;
            GetAllParticipants();

            SortGridView = new RelayCommand(SortParticipants, param=>true);
            ShowUpdateDetails = new RelayCommand(ShowUpdateWindow, param => SelectedParticipant != null);

            Mediator.Register("MainWindowClosing", OnMainWindowClosing);
        }

        void GetAllParticipants()
        {
            /*
            var participantVMs = Array.ConvertAll(_repository.Participants.ToArray(),
                new Converter<Participant, ParticipantListItemViewModel>(p => new ParticipantListItemViewModel(Mapper.Map<ParticipantModel>(p))));
             * */
            var participantVMs = _repository.Participants.Include("VaccinesAdministered").Include("VaccinesAdministered.VaccineGiven").ToArray()
                .Select(p => new ParticipantUpdateViewModel(_repository,Mapper.Map<ParticipantModel>(p)))
                .ToList();
            AllParticipants = new ListCollectionView(participantVMs);
            AllParticipants.GroupDescriptions.Add(new PropertyGroupDescription("DetailsPending"));
            AllParticipants.SortDescriptions.Add(new SortDescription("DataRequired", ListSortDirection.Ascending));
        }

        #endregion // Constructor

        #region Properties
        public ListCollectionView AllParticipants { get; private set; }
        public ParticipantUpdateViewModel SelectedParticipant
        { 
            get { return _selectedParticipant; } 
            set
            {
                if (_selectedParticipant == value) { return; }
                if (_updateWindow == null)
                {
                    _selectedParticipant = value;
                }
                else if (((ParticipantUpdateViewModel)_updateWindow.DataContext).OkToProceed())
                {
                    _updateWindow.DataContext = _selectedParticipant = value;
                }
                NotifyPropertyChanged("SelectedParticipant");
            }
        }
        

        #endregion

        #region Public Interface
        #endregion // Public Interface

        #region ICommands
        public RelayCommand ShowUpdateDetails { get; private set; }
        bool CanShowUpdateWindow(object param)
        {
            return _updateWindow == null;
        }
        void ShowUpdateWindow(object param)
        {
            _updateWindow = new ParticipantUpdateView(SelectedParticipant);
            _updateWindow.Closed += OnUpdateWindow_Closed;
            _updateWindow.Show();
        }

        public RelayCommand SortGridView { get; private set; }
        Dictionary<string,bool> _isAscending = new Dictionary<string,bool>();
        void SortParticipants(object param)
        {
            string propertyName = (string)param;
            bool isAscendingCol;
            if (!_isAscending.TryGetValue(propertyName, out isAscendingCol))
            {
                isAscendingCol = true;
                _isAscending.Add(propertyName, true);
            }
            AllParticipants.SortDescriptions.Clear();
            if (isAscendingCol)
            {
                AllParticipants.SortDescriptions.Add
		            (new SortDescription(propertyName, ListSortDirection.Ascending));
                _isAscending[propertyName] = false;
             }
            else
            {
                AllParticipants.SortDescriptions.Add
                   (new SortDescription(propertyName, ListSortDirection.Descending));
                _isAscending[propertyName] = true;
            }
        }
        #endregion

        #region Events

        private void OnParticipantAdded(object sender, ParticipantEventArgs e)
        {
            var viewModel = new ParticipantUpdateViewModel(_repository, Mapper.Map<ParticipantModel>(e.NewParticipant));
            AllParticipants.AddNewItem(viewModel);
            AllParticipants.CommitNew();
        }

        void OnUpdateWindow_Closed(object sender, EventArgs e)
        {
            _updateWindow.Closed -= OnUpdateWindow_Closed;
            _updateWindow = null;
        }

        void OnMainWindowClosing(object args)
        {
            if (_updateWindow == null) { return; }
            if (SelectedParticipant.OkToProceed())
            {
                _updateWindow.Close();
            }
            else
            {
                ((System.ComponentModel.CancelEventArgs)args).Cancel = true;
            }
        }

        #endregion // Events

        #region  finalizer
        ~AllParticipantsViewModel()
        {
            _repository.ParticipantAdded -= OnParticipantAdded;
            Mediator.Unregister("MainWindowClosing", OnMainWindowClosing);
        }

        #endregion // finalizer
    }
}